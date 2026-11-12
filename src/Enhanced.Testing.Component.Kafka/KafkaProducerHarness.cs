using Confluent.Kafka;

namespace Enhanced.Testing.Component.Kafka;

/// <summary>
///     A Kafka producer harness.
/// </summary>
/// <typeparam name="TKey">
///     The message key type.
/// </typeparam>
/// <typeparam name="TValue">
///     The message value type.
/// </typeparam>
public class KafkaProducerHarness<TKey, TValue> : Harness
{
    private readonly IKafkaHarness _kafkaHarness;
    private IProducer<TKey, TValue>? _producer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaProducerHarness{TKey, TValue}" /> class.
    /// </summary>
    /// <param name="kafkaHarness">
    ///     The Kafka harness.
    /// </param>
    public KafkaProducerHarness(KafkaHarness kafkaHarness)
        : this((IKafkaHarness)kafkaHarness)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaProducerHarness{TKey, TValue}" /> class.
    /// </summary>
    /// <param name="kafkaHarness">
    ///     The Kafka-compatible broker harness.
    /// </param>
    public KafkaProducerHarness(IKafkaHarness kafkaHarness)
    {
        ArgumentNullException.ThrowIfNull(kafkaHarness);
        _kafkaHarness = kafkaHarness;
    }

    /// <summary>
    ///     The topic to produce.
    /// </summary>
    public required string Topic { get; init; }

    /// <summary>
    ///     The key serializer.
    /// </summary>
    public ISerializer<TKey>? KeySerializer { get; set; }

    /// <summary>
    ///     The value serializer.
    /// </summary>
    public ISerializer<TValue>? ValueSerializer { get; set; }

    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     Produces a message.
    /// </summary>
    /// <param name="message">
    ///     The message to produce.
    /// </param>
    public void Produce(Message<TKey, TValue> message)
    {
        ThrowIfComponentNotStarted();
        _producer!.Produce(Topic, message);
    }

    /// <summary>
    ///     Produces a message asynchronously.
    /// </summary>
    /// <param name="message">
    ///     The message to produce.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    /// <returns>
    ///     A task that completes when the message is delivered.
    /// </returns>
    public Task ProduceAsync(Message<TKey, TValue> message, CancellationToken cancellationToken = default)
    {
        ThrowIfComponentNotStarted();
        return _producer!.ProduceAsync(Topic, message, cancellationToken);
    }

    /// <inheritdoc />
    protected override Task OnStart(CancellationToken cancellationToken)
    {
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = _kafkaHarness.GetConnectionString(), AllowAutoCreateTopics = false
        };

        _producer = new ProducerBuilder<TKey, TValue>(producerConfig)
                    .SetKeySerializer(KeySerializer)
                    .SetValueSerializer(ValueSerializer)
                    .Build();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task OnStop(CancellationToken cancellationToken)
    {
        var producer = _producer;
        _producer = null;

        if (producer is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            producer.Flush(FlushTimeout);
        }
        finally
        {
            producer.Dispose();
        }

        return Task.CompletedTask;
    }
}
