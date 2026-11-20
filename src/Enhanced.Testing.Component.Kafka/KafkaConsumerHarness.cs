using Confluent.Kafka;

namespace Enhanced.Testing.Component.Kafka;

/// <summary>
///     A Kafka consumer harness. It reads every partition the topic has when the component starts, starting at the
///     end of the topic. Partitions added later are not read.
/// </summary>
/// <typeparam name="TKey">
///     The message key type.
/// </typeparam>
/// <typeparam name="TValue">
///     The message value type.
/// </typeparam>
public class KafkaConsumerHarness<TKey, TValue> : Harness
{
    private readonly IKafkaHarness _kafkaHarness;
    private KafkaTopicReader<TKey, TValue>? _reader;

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
    ///     Moves the position of one partition to the specified offset, for example the one from a consumed message.
    ///     The positions of the other partitions do not change.
    /// </summary>
    /// <param name="position">
    ///     The partition and the offset to seek to: a message offset, <see cref="Offset.Beginning" /> or
    ///     <see cref="Offset.End" />.
    /// </param>
    /// <exception cref="ArgumentException">
    ///     Thrown if <paramref name="position" /> is not in a partition of the consumed topic, or its offset is a
    ///     special offset other than <see cref="Offset.Beginning" /> and <see cref="Offset.End" />.
    /// </exception>
    public void Seek(TopicPartitionOffset position) => Reader.Seek(position);

    internal KafkaTopicReader<TKey, TValue> Reader
    {
        get
        {
            ThrowIfComponentNotStarted();
            return _reader!;
        }
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
        return _reader!.Consumer.Consume(timeout);
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
        return _reader!.Consumer.Consume(cancellationToken);
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
        return _reader!.Consumer.Consume(millisecondsTimeout);
    }

    /// <summary>
    ///     Waits for a message that matches the predicate, skipping the messages that do not match.
    /// </summary>
    /// <remarks>
    ///     Messages are read from the current position: the end of the topic when the harness starts, or the offset set
    ///     by one of the seek methods. Matching on a unique key or value keeps the test independent of messages produced by
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
        return _reader!.WaitForMessageAsync(predicate, timeout, cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task OnStart(CancellationToken cancellationToken)
    {
        _reader = await KafkaTopicReader<TKey, TValue>.CreateAsync(_kafkaHarness.GetConnectionString(), Topic,
            KeyDeserializer, ValueDeserializer, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override Task OnStop(CancellationToken cancellationToken)
    {
        var reader = _reader;
        _reader = null;
        reader?.Dispose();
        return Task.CompletedTask;
    }
}
