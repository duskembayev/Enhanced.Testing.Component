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
            partitions = await GetPartitionsAsync(consumer, topic, cancellationToken).ConfigureAwait(false);
            var offsets = new List<TopicPartitionOffset>(partitions.Count);

            foreach (var partition in partitions)
            {
                var watermarks = await QueryWatermarkOffsetsAsync(consumer, partition, cancellationToken)
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
        var queryTimeout = timeout is null || timeout == Timeout.InfiniteTimeSpan ? StartTimeout : timeout.Value;

        if (offset.IsSpecial && offset != Offset.Beginning && offset != Offset.End)
        {
            throw new NotSupportedException("Offset kind is not supported.");
        }

        var offsets = new List<TopicPartitionOffset>(_partitions.Count);

        foreach (var partition in _partitions)
        {
            if (!offset.IsSpecial)
            {
                offsets.Add(new TopicPartitionOffset(partition, offset));
                continue;
            }

            var watermarks = Consumer.QueryWatermarkOffsets(partition, queryTimeout)
                             ?? throw new InvalidOperationException("Failed to query watermark offsets.");

            offsets.Add(new TopicPartitionOffset(partition, offset == Offset.Beginning ? watermarks.Low : watermarks.High));
        }

        Consumer.Assign(offsets);
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
        string topic, CancellationToken cancellationToken)
    {
        using var adminClient = new DependentAdminClientBuilder(consumer.Handle).Build();

        var deadline = DateTime.UtcNow + StartTimeout;
        var lastError = "no metadata received";

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

                lastError = topicMetadata?.Error.IsError == true
                    ? topicMetadata.Error.Reason
                    : "the topic has no partitions";
            }
            catch (KafkaException exception)
            {
                lastError = exception.Error.Reason;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Failed to get the partitions of Kafka topic '{topic}' within {StartTimeout.TotalSeconds:0} s: {lastError}.");
            }

            await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
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
