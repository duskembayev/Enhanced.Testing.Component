using Confluent.Kafka;

namespace Enhanced.Testing.Component.Kafka;

internal sealed class KafkaTopicReader<TKey, TValue> : IDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    private KafkaTopicReader(IConsumer<TKey, TValue> consumer, string topic)
    {
        Consumer = consumer;
        Topic = topic;
    }

    public IConsumer<TKey, TValue> Consumer { get; }

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
            FetchWaitMaxMs = 50,
            SocketNagleDisable = true
        };

        var consumer = new ConsumerBuilder<TKey, TValue>(consumerConfig)
                       .SetKeyDeserializer(keyDeserializer)
                       .SetValueDeserializer(valueDeserializer)
                       .Build();

        try
        {
            var partition = new TopicPartition(topic, 0);
            var watermarks = await QueryWatermarkOffsetsAsync(consumer, partition, cancellationToken)
                                 .ConfigureAwait(false);

            consumer.Assign(new TopicPartitionOffset(partition, watermarks.High));
        }
        catch
        {
            consumer.Dispose();
            throw;
        }

        return new KafkaTopicReader<TKey, TValue>(consumer, topic);
    }

    public void Seek(Offset offset, TimeSpan timeout)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            timeout = StartTimeout;
        }

        var tp = new TopicPartition(Topic, 0);
        var tpo = new TopicPartitionOffset(tp, offset);

        if (offset == Offset.Beginning)
        {
            var queryWatermarkOffsets = Consumer.QueryWatermarkOffsets(tp, timeout);

            if (queryWatermarkOffsets is null)
            {
                throw new InvalidOperationException("Failed to query watermark offsets.");
            }

            tpo = new TopicPartitionOffset(tp, queryWatermarkOffsets.Low);
        }
        else if (offset == Offset.End)
        {
            var queryWatermarkOffsets = Consumer.QueryWatermarkOffsets(tp, timeout);

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

        Consumer.Assign(tpo);
    }

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
            Consumer.Close();
        }
        finally
        {
            Consumer.Dispose();
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
                var result = Consumer.Consume(linkedSource.Token);

                if (result is null || result.IsPartitionEOF)
                {
                    continue;
                }

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
