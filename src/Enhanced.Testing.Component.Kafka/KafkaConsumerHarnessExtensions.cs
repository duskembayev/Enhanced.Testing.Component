using Confluent.Kafka;

namespace Enhanced.Testing.Component.Kafka;

/// <summary>
///     Kafka consumer harness extensions.
/// </summary>
public static class KafkaConsumerHarnessExtensions
{
    /// <summary>
    ///     Consumes a message.
    /// </summary>
    /// <param name="consumer">
    ///     The Kafka consumer.
    /// </param>
    /// <param name="millisecondsTimeout">
    ///     The timeout to wait for a message.
    /// </param>
    /// <typeparam name="TKey">
    ///     The message key type.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///     The message value type.
    /// </typeparam>
    /// <returns>
    ///     The consumed message.
    /// </returns>
    public static Message<TKey, TValue>? ConsumeMessage<TKey, TValue>(this KafkaConsumerHarness<TKey, TValue> consumer,
        int millisecondsTimeout = Timeout.Infinite)
    {
        var result = consumer.Consume(millisecondsTimeout);
        return result?.Message;
    }

    /// <summary>
    ///     Consumes a message.
    /// </summary>
    /// <param name="consumer">
    ///     The Kafka consumer.
    /// </param>
    /// <param name="timeout">
    ///     The timeout to wait for a message.
    /// </param>
    /// <typeparam name="TKey">
    ///     The message key type.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///     The message value type.
    /// </typeparam>
    /// <returns>
    ///     The consumed message.
    /// </returns>
    public static Message<TKey, TValue>? ConsumeMessage<TKey, TValue>(this KafkaConsumerHarness<TKey, TValue> consumer,
        TimeSpan timeout)
    {
        var result = consumer.Consume(timeout);
        return result?.Message;
    }

    /// <summary>
    ///     Consumes a message value.
    /// </summary>
    /// <param name="consumer">
    ///     The Kafka consumer.
    /// </param>
    /// <param name="millisecondsTimeout">
    ///     The timeout to wait for a message.
    /// </param>
    /// <typeparam name="TKey">
    ///     The message key type.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///     The message value type.
    /// </typeparam>
    /// <returns>
    ///     The consumed message.
    /// </returns>
    public static TValue? ConsumeValue<TKey, TValue>(this KafkaConsumerHarness<TKey, TValue> consumer,
        int millisecondsTimeout = Timeout.Infinite)
    {
        var result = consumer.Consume(millisecondsTimeout);

        if (result is null)
        {
            return default;
        }

        return result.Message.Value;
    }

    /// <summary>
    ///     Consumes a message value.
    /// </summary>
    /// <param name="consumer">
    ///     The Kafka consumer.
    /// </param>
    /// <param name="timeout">
    ///     The timeout to wait for a message.
    /// </param>
    /// <typeparam name="TKey">
    ///     The message key type.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///     The message value type.
    /// </typeparam>
    /// <returns>
    ///     The consumed message.
    /// </returns>
    public static TValue? ConsumeValue<TKey, TValue>(this KafkaConsumerHarness<TKey, TValue> consumer, TimeSpan timeout)
    {
        var result = consumer.Consume(timeout);

        if (result is null)
        {
            return default;
        }

        return result.Message.Value;
    }

    /// <summary>
    ///     Consumes a message key.
    /// </summary>
    /// <param name="consumer">
    ///     The Kafka consumer.
    /// </param>
    /// <param name="millisecondsTimeout">
    ///     The timeout to wait for a message.
    /// </param>
    /// <typeparam name="TKey">
    ///     The message key type.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///     The message value type.
    /// </typeparam>
    /// <returns>
    ///     The consumed message.
    /// </returns>
    public static TKey? ConsumeKey<TKey, TValue>(this KafkaConsumerHarness<TKey, TValue> consumer,
        int millisecondsTimeout = Timeout.Infinite)
    {
        var result = consumer.Consume(millisecondsTimeout);

        if (result is null)
        {
            return default;
        }

        return result.Message.Key;
    }

    /// <summary>
    ///     Consumes a message key.
    /// </summary>
    /// <param name="consumer">
    ///     The Kafka consumer.
    /// </param>
    /// <param name="timeout">
    ///     The timeout to wait for a message.
    /// </param>
    /// <typeparam name="TKey">
    ///     The message key type.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///     The message value type.
    /// </typeparam>
    /// <returns>
    ///     The consumed message.
    /// </returns>
    public static TKey? ConsumeKey<TKey, TValue>(this KafkaConsumerHarness<TKey, TValue> consumer, TimeSpan timeout)
    {
        var result = consumer.Consume(timeout);

        if (result is null)
        {
            return default;
        }

        return result.Message.Key;
    }

    /// <summary>
    ///     Seeks to the specified offset.
    /// </summary>
    /// <param name="consumer">
    ///     The Kafka consumer.
    /// </param>
    /// <param name="offset">
    ///     The offset to seek to.
    /// </param>
    /// <param name="millisecondsTimeout">
    ///     The timeout to wait for the operation to complete.
    /// </param>
    /// <typeparam name="TKey">
    ///     The message key type.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///     The message value type.
    /// </typeparam>
    public static void Seek<TKey, TValue>(this KafkaConsumerHarness<TKey, TValue> consumer, Offset offset,
        int millisecondsTimeout = Timeout.Infinite)
    {
        consumer.Seek(offset, TimeSpan.FromMilliseconds(millisecondsTimeout));
    }

    /// <summary>
    ///     Seeks to the beginning of the topic.
    /// </summary>
    /// <param name="consumer">
    ///     The Kafka consumer.
    /// </param>
    /// <typeparam name="TKey">
    ///     The message key type.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///     The message value type.
    /// </typeparam>
    public static void SeekToBeginning<TKey, TValue>(this KafkaConsumerHarness<TKey, TValue> consumer) =>
        consumer.Seek(Offset.Beginning);

    /// <summary>
    ///     Seeks to the end of the topic.
    /// </summary>
    /// <param name="consumer">
    ///     The Kafka consumer.
    /// </param>
    /// <typeparam name="TKey">
    ///     The message key type.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///     The message value type.
    /// </typeparam>
    public static void SeekToEnd<TKey, TValue>(this KafkaConsumerHarness<TKey, TValue> consumer) =>
        consumer.Seek(Offset.End);
}
