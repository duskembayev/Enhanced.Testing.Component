using Confluent.Kafka;

namespace Enhanced.Testing.Component.Kafka;

/// <summary>
///     Extensions that let tests share one broker while working with their own topics.
/// </summary>
public static class KafkaHarnessExtensions
{
    /// <summary>
    ///     Creates a topic with a unique name and waits until its partitions have a leader.
    /// </summary>
    /// <param name="broker">
    ///     The broker harness.
    /// </param>
    /// <param name="prefix">
    ///     The topic name prefix. A unique suffix is appended to it.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    /// <returns>
    ///     The name of the created topic.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the topic cannot be created.
    /// </exception>
    public static async Task<string> CreateUniqueTopicAsync(this IKafkaHarness broker, string prefix = "test",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        var topic = $"{prefix}-{Guid.NewGuid():N}";

        if (!await broker.CreateTopicAsync(topic, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Failed to create Kafka topic '{topic}'.");
        }

        return topic;
    }

    /// <summary>
    ///     Creates a consumer of the topic, positioned at the end of the topic.
    /// </summary>
    /// <param name="broker">
    ///     The broker harness.
    /// </param>
    /// <param name="topic">
    ///     The topic to consume. It must exist.
    /// </param>
    /// <param name="keyDeserializer">
    ///     The key deserializer, or null for the default one.
    /// </param>
    /// <param name="valueDeserializer">
    ///     The value deserializer, or null for the default one.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    /// <typeparam name="TKey">
    ///     The message key type.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///     The message value type.
    /// </typeparam>
    /// <returns>
    ///     The consumer. Dispose it when the test ends.
    /// </returns>
    public static async Task<KafkaTestConsumer<TKey, TValue>> CreateConsumerAsync<TKey, TValue>(
        this IKafkaHarness broker, string topic, IDeserializer<TKey>? keyDeserializer = null,
        IDeserializer<TValue>? valueDeserializer = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentException.ThrowIfNullOrEmpty(topic);

        var reader = await KafkaTopicReader<TKey, TValue>.CreateAsync(broker.GetConnectionString(), topic,
            keyDeserializer, valueDeserializer, cancellationToken).ConfigureAwait(false);

        return new KafkaTestConsumer<TKey, TValue>(reader);
    }

    /// <summary>
    ///     Creates a producer to the topic.
    /// </summary>
    /// <param name="broker">
    ///     The broker harness.
    /// </param>
    /// <param name="topic">
    ///     The topic to produce to. It must exist.
    /// </param>
    /// <param name="keySerializer">
    ///     The key serializer, or null for the default one.
    /// </param>
    /// <param name="valueSerializer">
    ///     The value serializer, or null for the default one.
    /// </param>
    /// <typeparam name="TKey">
    ///     The message key type.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///     The message value type.
    /// </typeparam>
    /// <returns>
    ///     The producer. Dispose it when the test ends.
    /// </returns>
    public static KafkaTestProducer<TKey, TValue> CreateProducer<TKey, TValue>(this IKafkaHarness broker,
        string topic, ISerializer<TKey>? keySerializer = null, ISerializer<TValue>? valueSerializer = null)
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentException.ThrowIfNullOrEmpty(topic);

        var producer = KafkaProducerFactory.Create(broker.GetConnectionString(), topic, keySerializer,
            valueSerializer);

        return new KafkaTestProducer<TKey, TValue>(producer, topic);
    }
}
