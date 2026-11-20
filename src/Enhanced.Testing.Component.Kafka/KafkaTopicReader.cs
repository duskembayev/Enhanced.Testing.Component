using Confluent.Kafka;

namespace Enhanced.Testing.Component.Kafka;

internal sealed class KafkaTopicReader<TKey, TValue> : IDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    private readonly Dictionary<TopicPartition, Offset> _assigned;
    private readonly IReadOnlyList<TopicPartition> _partitions;

    private KafkaTopicReader(IConsumer<TKey, TValue> consumer, string topic, IReadOnlyList<TopicPartitionOffset> assignment)
    {
        Consumer = consumer;
        Topic = topic;
        _partitions = assignment.Select(a => a.TopicPartition).ToList();
        _assigned = assignment.ToDictionary(a => a.TopicPartition, a => a.Offset);
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

        List<TopicPartitionOffset> assignment;

        try
        {
            var deadline = DateTime.UtcNow + StartTimeout;
            var partitions = await GetPartitionsAsync(consumer, topic, deadline, cancellationToken)
                                 .ConfigureAwait(false);

            assignment = new List<TopicPartitionOffset>(partitions.Count);

            foreach (var partition in partitions)
            {
                var watermarks = await QueryWatermarkOffsetsAsync(consumer, partition, deadline, cancellationToken)
                                     .ConfigureAwait(false);

                assignment.Add(new TopicPartitionOffset(partition, watermarks.High));
            }

            consumer.Assign(assignment);
        }
        catch
        {
            consumer.Dispose();
            throw;
        }

        return new KafkaTopicReader<TKey, TValue>(consumer, topic, assignment);
    }

    public void SeekToBeginning() => SeekEveryPartition(Offset.Beginning);

    public void SeekToEnd() => SeekEveryPartition(Offset.End);

    public void SeekToBeginning(TopicPartition partition)
    {
        ArgumentNullException.ThrowIfNull(partition);
        Seek(new TopicPartitionOffset(partition, Offset.Beginning));
    }

    public void SeekToEnd(TopicPartition partition)
    {
        ArgumentNullException.ThrowIfNull(partition);
        Seek(new TopicPartitionOffset(partition, Offset.End));
    }

    public void Seek(TopicPartitionOffset position)
    {
        ArgumentNullException.ThrowIfNull(position);

        if (position.Topic != Topic || !_partitions.Contains(position.TopicPartition))
        {
            throw new ArgumentException(
                $"The position must be in a partition of Kafka topic '{Topic}' that the consumer reads.",
                nameof(position));
        }

        if (position.Offset.IsSpecial && position.Offset != Offset.Beginning && position.Offset != Offset.End)
        {
            throw new ArgumentException(
                "The offset must be a message offset, Offset.Beginning or Offset.End.", nameof(position));
        }

        var deadline = DateTime.UtcNow + StartTimeout;
        var offset = position.Offset.IsSpecial
            ? GetWatermark(position.TopicPartition, position.Offset, deadline)
            : position.Offset;

        Assign(_partitions.Select(p => new TopicPartitionOffset(p, p == position.TopicPartition ? offset : GetPosition(p))));
    }

    private void SeekEveryPartition(Offset offset)
    {
        var deadline = DateTime.UtcNow + StartTimeout;
        Assign(_partitions.Select(p => new TopicPartitionOffset(p, GetWatermark(p, offset, deadline))));
    }

    private void Assign(IEnumerable<TopicPartitionOffset> offsets)
    {
        var assignment = offsets.ToList();

        Consumer.Assign(assignment);

        foreach (var position in assignment)
        {
            _assigned[position.TopicPartition] = position.Offset;
        }
    }

    private Offset GetPosition(TopicPartition partition)
    {
        var position = Consumer.Position(partition);
        return position.IsSpecial ? _assigned[partition] : position;
    }

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
