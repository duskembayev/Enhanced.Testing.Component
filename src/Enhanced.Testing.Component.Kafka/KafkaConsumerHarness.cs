using Confluent.Kafka;

namespace Enhanced.Testing.Component.Kafka;

/// <summary>
///     A Kafka consumer harness.
/// </summary>
/// <param name="kafkaHarness">
///     The Kafka harness.
/// </param>
/// <typeparam name="TKey">
///     The message key type.
/// </typeparam>
/// <typeparam name="TValue">
///     The message value type.
/// </typeparam>
public class KafkaConsumerHarness<TKey, TValue>(KafkaHarness kafkaHarness) : Harness
{
    private IConsumer<TKey, TValue>? _consumer;

    /// <summary>
    ///     The topic to consume.
    /// </summary>
    public required string Topic { get; init; }

    /// <summary>
    ///     The key deserializer.
    /// </summary>
    public IDeserializer<TKey>? KeyDeserializer { get; set; }

    /// <summary>
    ///     The value deserializer.
    /// </summary>
    public IDeserializer<TValue>? ValueDeserializer { get; set; }

    /// <summary>
    ///     Seek to the specified offset.
    /// </summary>
    /// <param name="offset">
    ///     The offset to seek to.
    /// </param>
    /// <param name="timeout">
    ///     The timeout to wait for the operation.
    /// </param>
    public void Seek(Offset offset, TimeSpan timeout)
    {
        ThrowIfComponentNotStarted();

        var tp = new TopicPartition(Topic, 0);
        var tpo = new TopicPartitionOffset(tp, offset);

        if (offset == Offset.Beginning)
        {
            var queryWatermarkOffsets = _consumer!.QueryWatermarkOffsets(tp, timeout);

            if (queryWatermarkOffsets is null)
            {
                throw new InvalidOperationException("Failed to query watermark offsets.");
            }

            tpo = new TopicPartitionOffset(tp, queryWatermarkOffsets.Low);
        }
        else if (offset == Offset.End)
        {
            var queryWatermarkOffsets = _consumer!.QueryWatermarkOffsets(tp, timeout);

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

        _consumer!.Seek(tpo);
    }

    /// <summary>
    ///     Consume a message.
    /// </summary>
    /// <param name="timeout">
    ///     The timeout to wait for a message.
    /// </param>
    /// <returns>
    ///     The consumed message.
    /// </returns>
    public ConsumeResult<TKey, TValue>? Consume(TimeSpan timeout)
    {
        ThrowIfComponentNotStarted();
        return _consumer!.Consume(timeout);
    }

    /// <summary>
    ///     Consume a message.
    /// </summary>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    /// <returns>
    ///     The consumed message.
    /// </returns>
    public ConsumeResult<TKey, TValue>? Consume(CancellationToken cancellationToken)
    {
        ThrowIfComponentNotStarted();
        return _consumer!.Consume(cancellationToken);
    }

    /// <summary>
    ///     Consume a message.
    /// </summary>
    /// <param name="millisecondsTimeout">
    ///     The timeout to wait for a message.
    /// </param>
    /// <returns>
    ///     The consumed message.
    /// </returns>
    public ConsumeResult<TKey, TValue>? Consume(int millisecondsTimeout = Timeout.Infinite)
    {
        ThrowIfComponentNotStarted();
        return _consumer!.Consume(millisecondsTimeout);
    }

    /// <inheritdoc />
    protected override Task OnStart(CancellationToken cancellationToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = kafkaHarness.GetConnectionString(),
            GroupId = Guid.NewGuid().ToString("N"),
            AllowAutoCreateTopics = false,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Latest
        };

        var consumerBuilder = new ConsumerBuilder<TKey, TValue>(consumerConfig)
                              .SetKeyDeserializer(KeyDeserializer)
                              .SetValueDeserializer(ValueDeserializer);

        _consumer = consumerBuilder.Build();
        _consumer.Assign(new TopicPartitionOffset(Topic, 0, Offset.End));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task OnStop(CancellationToken cancellationToken)
    {
        _consumer?.Close();
        _consumer?.Dispose();
        return Task.CompletedTask;
    }
}
