using Confluent.Kafka;

namespace Enhanced.Testing.Component.Kafka;

/// <summary>
///     Kafka producer harness extensions.
/// </summary>
public static class KafkaProducerHarnessExtensions
{
    /// <summary>
    ///     Produces a message.
    /// </summary>
    /// <param name="producer">
    ///     The Kafka producer.
    /// </param>
    /// <param name="key">
    ///     The message key.
    /// </param>
    /// <param name="value">
    ///     The message value.
    /// </param>
    /// <typeparam name="TKey">
    ///     The message key type.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///     The message value type.
    /// </typeparam>
    public static void Produce<TKey, TValue>(this KafkaProducerHarness<TKey, TValue> producer, TKey key, TValue value)
    {
        var message = new Message<TKey, TValue> { Key = key, Value = value };

        producer.Produce(message);
    }

    /// <summary>
    ///     Produces a message asynchronously.
    /// </summary>
    /// <param name="producer">
    ///     The Kafka producer.
    /// </param>
    /// <param name="key">
    ///     The message key.
    /// </param>
    /// <param name="value">
    ///     The message value.
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
    /// <returns></returns>
    public static Task ProduceAsync<TKey, TValue>(this KafkaProducerHarness<TKey, TValue> producer, TKey key,
        TValue value,
        CancellationToken cancellationToken = default)
    {
        var message = new Message<TKey, TValue> { Key = key, Value = value };

        return producer.ProduceAsync(message, cancellationToken);
    }
}
