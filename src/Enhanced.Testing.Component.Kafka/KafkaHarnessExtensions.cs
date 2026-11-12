using Confluent.Kafka;

namespace Enhanced.Testing.Component.Kafka;

/// <summary>
///     Extensions that let tests share one broker while working with their own topics.
/// </summary>
public static class KafkaHarnessExtensions
{
    private const int MaxTopicNameLength = 249;
    private const int UniqueSuffixLength = 33;

    /// <summary>
    ///     Creates a topic with a unique name and waits until its partitions have a leader.
    /// </summary>
    /// <param name="broker">
    ///     The broker harness.
    /// </param>
    /// <param name="prefix">
    ///     The topic name prefix. A unique suffix is appended to it. It may contain only ASCII letters, digits,
    ///     <c>.</c>, <c>_</c> and <c>-</c>, and be at most 216 characters long.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    /// <returns>
    ///     The name of the created topic.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="prefix" /> is empty, too long or contains characters not allowed in a topic name.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the topic cannot be created.
    /// </exception>
    public static async Task<string> CreateUniqueTopicAsync(this IKafkaHarness broker, string prefix = "test",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        if (prefix.Length > MaxTopicNameLength - UniqueSuffixLength)
        {
            throw new ArgumentException(
                $"The prefix must be at most {MaxTopicNameLength - UniqueSuffixLength} characters long.",
                nameof(prefix));
        }

        if (!prefix.All(IsValidTopicNameChar))
        {
            throw new ArgumentException(
                "The prefix may contain only ASCII letters, digits, '.', '_' and '-'.", nameof(prefix));
        }

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
    /// <remarks>
    ///     The consumer reads partition 0 only, so the topic must have a single partition. Topics created by
    ///     <see cref="CreateUniqueTopicAsync" /> get the broker's default partition count, which is 1 unless the
    ///     broker is configured otherwise.
    /// </remarks>
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
    /// <exception cref="KafkaException">
    ///     Thrown when the topic offsets cannot be queried within 30 seconds, for example because the topic does not
    ///     exist.
    /// </exception>
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

    private static bool IsValidTopicNameChar(char c)
    {
        return char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-';
    }
}
