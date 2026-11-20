using Confluent.Kafka;

namespace Enhanced.Testing.Component.Kafka;

internal sealed class KafkaTopicReader<TKey, TValue> : IDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    private readonly IConsumer<TKey, TValue> _consumer;
    private readonly TopicPartition _partition;
    private Offset _position;

    private KafkaTopicReader(IConsumer<TKey, TValue> consumer, TopicPartitionOffset start)
    {
        _consumer = consumer;
        _partition = start.TopicPartition;
        _position = start.Offset;
        Topic = start.Topic;
    }

    public string Topic { get; }

    public static async Task<KafkaTopicReader<TKey, TValue>> CreateAsync(string bootstrapServers, string topic,
        IDeserializer<TKey>? keyDeserializer, IDeserializer<TValue>? valueDeserializer,
        CancellationToken cancellationToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = Guid.NewGuid().ToString("N"),
            AllowAutoCreateTopics = false,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnablePartitionEof = true,
            FetchWaitMaxMs = 50,
            SocketNagleDisable = true
        };

        var consumer = new ConsumerBuilder<TKey, TValue>(consumerConfig)
                       .SetKeyDeserializer(keyDeserializer)
                       .SetValueDeserializer(valueDeserializer)
                       .Build();

        TopicPartitionOffset start;

        try
        {
            var partition = new TopicPartition(topic, 0);
            var watermarks = await QueryWatermarkOffsetsAsync(consumer, partition, cancellationToken)
                                 .ConfigureAwait(false);

            start = new TopicPartitionOffset(partition, watermarks.High);
            consumer.Assign(start);
        }
        catch
        {
            consumer.Dispose();
            throw;
        }

        return new KafkaTopicReader<TKey, TValue>(consumer, start);
    }

    public void Seek(Offset offset, TimeSpan timeout)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            timeout = StartTimeout;
        }

        var tp = _partition;
        var tpo = new TopicPartitionOffset(tp, offset);

        if (offset == Offset.Beginning)
        {
            var queryWatermarkOffsets = _consumer.QueryWatermarkOffsets(tp, timeout);

            if (queryWatermarkOffsets is null)
            {
                throw new InvalidOperationException("Failed to query watermark offsets.");
            }

            tpo = new TopicPartitionOffset(tp, queryWatermarkOffsets.Low);
        }
        else if (offset == Offset.End)
        {
            var queryWatermarkOffsets = _consumer.QueryWatermarkOffsets(tp, timeout);

            if (queryWatermarkOffsets is null)
            {
                throw new InvalidOperationException("Failed to query watermark offsets.");
            }

            tpo = new TopicPartitionOffset(tp, queryWatermarkOffsets.High);
        }
        else if (offset.IsSpecial)
        {
            throw new NotSupportedException("Offset kind is not supported.");
        }

        _consumer.Assign(tpo);
        _position = tpo.Offset;
    }

    public ConsumeResult<TKey, TValue>? Consume(TimeSpan timeout)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return Consume(CancellationToken.None);
        }

        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            var result = _consumer.Consume(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);

            if (result is null)
            {
                return null;
            }

            if (Track(result))
            {
                return result;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return null;
            }
        }
    }

    public ConsumeResult<TKey, TValue> Consume(CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = _consumer.Consume(cancellationToken);

            if (result is not null && Track(result))
            {
                return result;
            }
        }
    }

    public Task<IReadOnlyList<ConsumeResult<TKey, TValue>>> ReadToEndAsync(CancellationToken cancellationToken) =>
        Task.Run(() => ReadToEnd(cancellationToken), cancellationToken);

    public Task<ConsumeResult<TKey, TValue>> WaitForMessageAsync(Func<ConsumeResult<TKey, TValue>, bool> predicate,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Task.Run(() => WaitForMessage(predicate, timeout, cancellationToken), cancellationToken);
    }

    public void Dispose()
    {
        try
        {
            _consumer.Close();
        }
        finally
        {
            _consumer.Dispose();
        }
    }

    private ConsumeResult<TKey, TValue> WaitForMessage(Func<ConsumeResult<TKey, TValue>, bool> predicate,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        var skipped = 0;

        try
        {
            while (true)
            {
                var result = Consume(linkedSource.Token);

                if (predicate(result))
                {
                    return result;
                }

                skipped++;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"No matching message arrived in topic '{Topic}' within {timeout.TotalMilliseconds:0} ms " +
                $"({skipped} non-matching message(s) skipped).");
        }
    }

    private IReadOnlyList<ConsumeResult<TKey, TValue>> ReadToEnd(CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(StartTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        var end = _consumer.QueryWatermarkOffsets(_partition, QueryTimeout).High;
        var results = new List<ConsumeResult<TKey, TValue>>();

        try
        {
            while (_position < end)
            {
                var result = _consumer.Consume(linkedSource.Token);

                if (result is null)
                {
                    continue;
                }

                if (!result.IsPartitionEOF && result.Offset >= end)
                {
                    _consumer.Assign(result.TopicPartitionOffset);
                    _position = result.Offset;
                    break;
                }

                if (Track(result))
                {
                    results.Add(result);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The end of topic '{Topic}' (offset {end.Value}) was not reached within " +
                $"{StartTimeout.TotalSeconds:0} s; the position is {_position.Value}.");
        }

        return results;
    }

    private bool Track(ConsumeResult<TKey, TValue> result)
    {
        if (result.IsPartitionEOF)
        {
            if (result.Offset > _position)
            {
                _position = result.Offset;
            }

            return false;
        }

        _position = result.Offset + 1;
        return true;
    }

    private static async Task<WatermarkOffsets> QueryWatermarkOffsetsAsync(IConsumer<TKey, TValue> consumer,
        TopicPartition partition, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + StartTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return consumer.QueryWatermarkOffsets(partition, QueryTimeout);
            }
            catch (KafkaException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
