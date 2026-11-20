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
    ///     Moves the position of every partition to the beginning of the topic.
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
        consumer.Reader.SeekToBeginning();

    /// <summary>
    ///     Moves the position of one partition to its beginning. The positions of the other partitions do not change.
    /// </summary>
    /// <param name="consumer">
    ///     The Kafka consumer.
    /// </param>
    /// <param name="partition">
    ///     The partition to move.
    /// </param>
    /// <typeparam name="TKey">
    ///     The message key type.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///     The message value type.
    /// </typeparam>
    /// <exception cref="ArgumentException">
    ///     Thrown if <paramref name="partition" /> is not a partition of the consumed topic.
    /// </exception>
    public static void SeekToBeginning<TKey, TValue>(this KafkaConsumerHarness<TKey, TValue> consumer,
        TopicPartition partition) =>
        consumer.Reader.SeekToBeginning(partition);

    /// <summary>
    ///     Moves the position of every partition to the end of the topic.
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
        consumer.Reader.SeekToEnd();

    /// <summary>
    ///     Moves the position of one partition to its end. The positions of the other partitions do not change.
    /// </summary>
    /// <param name="consumer">
    ///     The Kafka consumer.
    /// </param>
    /// <param name="partition">
    ///     The partition to move.
    /// </param>
    /// <typeparam name="TKey">
    ///     The message key type.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///     The message value type.
    /// </typeparam>
    /// <exception cref="ArgumentException">
    ///     Thrown if <paramref name="partition" /> is not a partition of the consumed topic.
    /// </exception>
    public static void SeekToEnd<TKey, TValue>(this KafkaConsumerHarness<TKey, TValue> consumer,
        TopicPartition partition) =>
        consumer.Reader.SeekToEnd(partition);

    /// <summary>
    ///     Waits for a message with the specified key, skipping messages with other keys.
    /// </summary>
    /// <param name="consumer">
    ///     The Kafka consumer.
    /// </param>
    /// <param name="key">
    ///     The message key.
    /// </param>
    /// <param name="timeout">
    ///     The maximum time to wait for the message.
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
    ///     The first message with the key.
    /// </returns>
    /// <exception cref="TimeoutException">
    ///     Thrown when no message with the key arrives within <paramref name="timeout" />.
    /// </exception>
    public static async Task<Message<TKey, TValue>> WaitForKeyAsync<TKey, TValue>(
        this KafkaConsumerHarness<TKey, TValue> consumer, TKey key, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var comparer = EqualityComparer<TKey>.Default;
        var result = await consumer.WaitForMessageAsync(r => comparer.Equals(r.Message.Key, key), timeout,
            cancellationToken).ConfigureAwait(false);

        return result.Message;
    }
}
