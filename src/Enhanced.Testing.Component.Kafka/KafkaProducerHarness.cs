using Confluent.Kafka;

namespace Enhanced.Testing.Component.Kafka;

/// <summary>
///     A Kafka producer harness.
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
public class KafkaProducerHarness<TKey, TValue>(KafkaHarness kafkaHarness) : Harness
{
    private IProducer<TKey, TValue>? _producer;

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
    /// <returns></returns>
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
            BootstrapServers = kafkaHarness.GetConnectionString(), AllowAutoCreateTopics = false
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
        _producer?.Dispose();
        return Task.CompletedTask;
    }
}
