using Confluent.Kafka;

namespace Enhanced.Testing.Component.Kafka;

/// <summary>
///     A Kafka consumer harness.
/// </summary>
/// <typeparam name="TKey">
///     The message key type.
/// </typeparam>
/// <typeparam name="TValue">
///     The message value type.
/// </typeparam>
public class KafkaConsumerHarness<TKey, TValue> : Harness
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    private readonly IKafkaHarness _kafkaHarness;
    private IConsumer<TKey, TValue>? _consumer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaConsumerHarness{TKey, TValue}" /> class.
    /// </summary>
    /// <param name="kafkaHarness">
    ///     The Kafka harness.
    /// </param>
    public KafkaConsumerHarness(KafkaHarness kafkaHarness)
        : this((IKafkaHarness)kafkaHarness)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaConsumerHarness{TKey, TValue}" /> class.
    /// </summary>
    /// <param name="kafkaHarness">
    ///     The Kafka-compatible broker harness.
    /// </param>
    public KafkaConsumerHarness(IKafkaHarness kafkaHarness)
    {
        ArgumentNullException.ThrowIfNull(kafkaHarness);
        _kafkaHarness = kafkaHarness;
    }

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
    ///     The timeout to wait for the operation. An infinite timeout is limited to 30 seconds.
    /// </param>
    public void Seek(Offset offset, TimeSpan timeout)
    {
        ThrowIfComponentNotStarted();

        if (timeout == Timeout.InfiniteTimeSpan)
        {
            timeout = StartTimeout;
        }

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

        _consumer!.Assign(tpo);
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

    /// <summary>
    ///     Waits for a message that matches the predicate, skipping the messages that do not match.
    /// </summary>
    /// <remarks>
    ///     Messages are read from the current position: the end of the topic when the harness starts, or the offset set
    ///     by <see cref="Seek" />. Matching on a unique key or value keeps the test independent of messages produced by
    ///     earlier tests.
    /// </remarks>
    /// <param name="predicate">
    ///     The condition the message must satisfy.
    /// </param>
    /// <param name="timeout">
    ///     The maximum time to wait for a matching message.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    /// <returns>
    ///     The first matching message.
    /// </returns>
    /// <exception cref="TimeoutException">
    ///     Thrown when no matching message arrives within <paramref name="timeout" />.
    /// </exception>
    public Task<ConsumeResult<TKey, TValue>> WaitForMessageAsync(Func<ConsumeResult<TKey, TValue>, bool> predicate,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ThrowIfComponentNotStarted();
        ArgumentNullException.ThrowIfNull(predicate);

        var consumer = _consumer!;
        return Task.Run(() => WaitForMessage(consumer, predicate, timeout, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task OnStart(CancellationToken cancellationToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _kafkaHarness.GetConnectionString(),
            GroupId = Guid.NewGuid().ToString("N"),
            AllowAutoCreateTopics = false,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Latest,
            FetchWaitMaxMs = 50,
            SocketNagleDisable = true
        };

        var consumerBuilder = new ConsumerBuilder<TKey, TValue>(consumerConfig)
                              .SetKeyDeserializer(KeyDeserializer)
                              .SetValueDeserializer(ValueDeserializer);

        _consumer = consumerBuilder.Build();

        var partition = new TopicPartition(Topic, 0);
        var watermarks = await QueryWatermarkOffsetsAsync(_consumer, partition, cancellationToken).ConfigureAwait(false);

        _consumer.Assign(new TopicPartitionOffset(partition, watermarks.High));
    }

    /// <inheritdoc />
    protected override Task OnStop(CancellationToken cancellationToken)
    {
        var consumer = _consumer;

        if (consumer is null)
        {
            return Task.CompletedTask;
        }

        _consumer = null;

        try
        {
            consumer.Close();
        }
        finally
        {
            consumer.Dispose();
        }

        return Task.CompletedTask;
    }

    private ConsumeResult<TKey, TValue> WaitForMessage(IConsumer<TKey, TValue> consumer,
        Func<ConsumeResult<TKey, TValue>, bool> predicate, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        var skipped = 0;

        try
        {
            while (true)
            {
                var result = consumer.Consume(linkedSource.Token);

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
