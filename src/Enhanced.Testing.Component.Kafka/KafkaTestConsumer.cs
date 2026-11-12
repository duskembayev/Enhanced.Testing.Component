using Confluent.Kafka;

namespace Enhanced.Testing.Component.Kafka;

/// <summary>
///     A consumer of a single topic created during a test. It is positioned at the end of the topic when created.
/// </summary>
/// <remarks>
///     Create it with
///     <see cref="KafkaHarnessExtensions.CreateConsumerAsync{TKey, TValue}" /> and dispose it at the end of the test.
///     It reads partition 0 of the topic only. It is not thread-safe: do not consume, wait or seek concurrently.
/// </remarks>
/// <typeparam name="TKey">
///     The message key type.
/// </typeparam>
/// <typeparam name="TValue">
///     The message value type.
/// </typeparam>
public sealed class KafkaTestConsumer<TKey, TValue> : IDisposable
{
    private readonly KafkaTopicReader<TKey, TValue> _reader;
    private int _disposed;

    internal KafkaTestConsumer(KafkaTopicReader<TKey, TValue> reader)
    {
        _reader = reader;
    }

    /// <summary>
    ///     The consumed topic.
    /// </summary>
    public string Topic => _reader.Topic;

    /// <summary>
    ///     Consumes the next message.
    /// </summary>
    /// <param name="timeout">
    ///     The timeout to wait for a message.
    /// </param>
    /// <returns>
    ///     The consumed message, or null if no message arrived.
    /// </returns>
    public ConsumeResult<TKey, TValue>? Consume(TimeSpan timeout)
    {
        ThrowIfDisposed();
        return _reader.Consumer.Consume(timeout);
    }

    /// <summary>
    ///     Waits for a message that matches the predicate, skipping the messages that do not match.
    /// </summary>
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
        ThrowIfDisposed();
        return _reader.WaitForMessageAsync(predicate, timeout, cancellationToken);
    }

    /// <summary>
    ///     Waits for a message with the specified key, skipping messages with other keys.
    /// </summary>
    /// <param name="key">
    ///     The message key.
    /// </param>
    /// <param name="timeout">
    ///     The maximum time to wait for the message.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    /// <returns>
    ///     The first message with the key.
    /// </returns>
    /// <exception cref="TimeoutException">
    ///     Thrown when no message with the key arrives within <paramref name="timeout" />.
    /// </exception>
    public async Task<Message<TKey, TValue>> WaitForKeyAsync(TKey key, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var comparer = EqualityComparer<TKey>.Default;
        var result = await WaitForMessageAsync(r => comparer.Equals(r.Message.Key, key), timeout, cancellationToken)
                         .ConfigureAwait(false);

        return result.Message;
    }

    /// <summary>
    ///     Moves the position to the beginning of the topic.
    /// </summary>
    public void SeekToBeginning()
    {
        ThrowIfDisposed();
        _reader.Seek(Offset.Beginning, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    ///     Moves the position to the end of the topic.
    /// </summary>
    public void SeekToEnd()
    {
        ThrowIfDisposed();
        _reader.Seek(Offset.End, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    ///     Closes the consumer.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _reader.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
    }
}
