using Confluent.Kafka;

namespace Enhanced.Testing.Component.Kafka;

internal sealed class KafkaTopicReader<TKey, TValue> : IDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    private readonly IReadOnlyList<TopicPartition> _partitions;

    private KafkaTopicReader(IConsumer<TKey, TValue> consumer, string topic, IReadOnlyList<TopicPartition> partitions)
    {
        Consumer = consumer;
        Topic = topic;
        _partitions = partitions;
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

        IReadOnlyList<TopicPartition> partitions;

        try
        {
            var deadline = DateTime.UtcNow + StartTimeout;
            partitions = await GetPartitionsAsync(consumer, topic, deadline, cancellationToken).ConfigureAwait(false);
            var offsets = new List<TopicPartitionOffset>(partitions.Count);

            foreach (var partition in partitions)
            {
                var watermarks = await QueryWatermarkOffsetsAsync(consumer, partition, deadline, cancellationToken)
                                     .ConfigureAwait(false);

                offsets.Add(new TopicPartitionOffset(partition, watermarks.High));
            }

            consumer.Assign(offsets);
        }
        catch
        {
            consumer.Dispose();
            throw;
        }

        return new KafkaTopicReader<TKey, TValue>(consumer, topic, partitions);
    }

    public void Seek(Offset offset, TimeSpan? timeout)
    {
        if (!offset.IsSpecial)
        {
            if (_partitions.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Kafka topic '{Topic}' has {_partitions.Count} partitions. " +
                    "Seek to an offset of a specific partition with Seek(TopicPartitionOffset).");
            }

            Consumer.Assign(new TopicPartitionOffset(_partitions[0], offset));
            return;
        }

        if (offset != Offset.Beginning && offset != Offset.End)
        {
            throw new NotSupportedException("Offset kind is not supported.");
        }

        var deadline = GetDeadline(timeout);
        var offsets = _partitions.Select(p => new TopicPartitionOffset(p, GetWatermark(p, offset, deadline))).ToList();
        Consumer.Assign(offsets);
    }

    public void Seek(TopicPartitionOffset position, TimeSpan? timeout)
    {
        ArgumentNullException.ThrowIfNull(position);

        if (position.Topic != Topic || !_partitions.Contains(position.TopicPartition))
        {
            throw new ArgumentException(
                $"The position must be in a partition of Kafka topic '{Topic}' known to the consumer.", nameof(position));
        }

        if (position.Offset.IsSpecial && position.Offset != Offset.Beginning && position.Offset != Offset.End)
        {
            throw new NotSupportedException("Offset kind is not supported.");
        }

        var deadline = GetDeadline(timeout);
        var offsets = _partitions.Select(p => p == position.TopicPartition
                                     ? new TopicPartitionOffset(p,
                                         position.Offset.IsSpecial ? GetWatermark(p, position.Offset, deadline) : position.Offset)
                                     : new TopicPartitionOffset(p, GetWatermark(p, Offset.End, deadline)))
                                 .ToList();

        Consumer.Assign(offsets);
    }

    private static DateTime GetDeadline(TimeSpan? timeout) =>
        DateTime.UtcNow + (timeout is null || timeout == Timeout.InfiniteTimeSpan ? StartTimeout : timeout.Value);

    private Offset GetWatermark(TopicPartition partition, Offset offset, DateTime deadline)
    {
        var remaining = deadline - DateTime.UtcNow;

        if (remaining <= TimeSpan.Zero)
        {
            throw new TimeoutException($"Failed to query the offsets of Kafka topic '{Topic}' in time.");
        }

        var watermarks = Consumer.QueryWatermarkOffsets(partition, remaining);
        return offset == Offset.Beginning ? watermarks.Low : watermarks.High;
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

    private static async Task<IReadOnlyList<TopicPartition>> GetPartitionsAsync(IConsumer<TKey, TValue> consumer,
        string topic, DateTime deadline, CancellationToken cancellationToken)
    {
        using var adminClient = new DependentAdminClientBuilder(consumer.Handle).Build();

        Error? lastError = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var metadata = adminClient.GetMetadata(topic, QueryTimeout);
                var topicMetadata = metadata.Topics.FirstOrDefault(t => t.Topic == topic);

                if (topicMetadata is { Error.IsError: false, Partitions.Count: > 0 })
                {
                    return topicMetadata.Partitions
                                        .Select(p => new TopicPartition(topic, p.PartitionId))
                                        .OrderBy(p => p.Partition.Value)
                                        .ToList();
                }

                lastError = topicMetadata?.Error.IsError == true ? topicMetadata.Error : null;
            }
            catch (KafkaException exception)
            {
                lastError = exception.Error;
            }

            if (DateTime.UtcNow >= deadline)
            {
                if (lastError is not null)
                {
                    throw new KafkaException(lastError);
                }

                throw new TimeoutException(
                    $"Kafka topic '{topic}' has no partitions after {StartTimeout.TotalSeconds:0} s.");
            }

            await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<WatermarkOffsets> QueryWatermarkOffsetsAsync(IConsumer<TKey, TValue> consumer,
        TopicPartition partition, DateTime deadline, CancellationToken cancellationToken)
    {
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
