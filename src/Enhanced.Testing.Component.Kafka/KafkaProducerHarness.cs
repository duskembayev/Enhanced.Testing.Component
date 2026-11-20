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
    ///     The delivery result.
    /// </returns>
    public Task<DeliveryResult<TKey, TValue>> ProduceAsync(Message<TKey, TValue> message, CancellationToken cancellationToken = default)
    {
        ThrowIfComponentNotStarted();
        return _producer!.ProduceAsync(Topic, message, cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task OnStart(CancellationToken cancellationToken)
    {
        _producer = await KafkaProducerFactory.CreateAsync(_kafkaHarness.GetConnectionString(), Topic, KeySerializer,
            ValueSerializer, cancellationToken).ConfigureAwait(false);
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
            producer.Flush(KafkaProducerFactory.FlushTimeout);
        }
        finally
        {
            producer.Dispose();
        }

        return Task.CompletedTask;
    }
}
