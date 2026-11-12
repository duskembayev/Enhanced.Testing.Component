using Confluent.Kafka;

namespace Enhanced.Testing.Component.Kafka;

/// <summary>
///     A producer to a single topic created during a test.
/// </summary>
/// <remarks>
///     Create it with <see cref="KafkaHarnessExtensions.CreateProducer{TKey, TValue}" /> and dispose it at the end of the
///     test.
/// </remarks>
/// <typeparam name="TKey">
///     The message key type.
/// </typeparam>
/// <typeparam name="TValue">
///     The message value type.
/// </typeparam>
public sealed class KafkaTestProducer<TKey, TValue> : IDisposable
{
    private readonly IProducer<TKey, TValue> _producer;
    private int _disposed;

    internal KafkaTestProducer(IProducer<TKey, TValue> producer, string topic)
    {
        _producer = producer;
        Topic = topic;
    }

    /// <summary>
    ///     The topic messages are produced to.
    /// </summary>
    public string Topic { get; }

    /// <summary>
    ///     Produces a message and waits until it is delivered.
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
    public Task<DeliveryResult<TKey, TValue>> ProduceAsync(Message<TKey, TValue> message,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        return _producer.ProduceAsync(Topic, message, cancellationToken);
    }

    /// <summary>
    ///     Produces a message and waits until it is delivered.
    /// </summary>
    /// <param name="key">
    ///     The message key.
    /// </param>
    /// <param name="value">
    ///     The message value.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    /// <returns>
    ///     The delivery result.
    /// </returns>
    public Task<DeliveryResult<TKey, TValue>> ProduceAsync(TKey key, TValue value,
        CancellationToken cancellationToken = default)
    {
        return ProduceAsync(new Message<TKey, TValue> { Key = key, Value = value }, cancellationToken);
    }

    /// <summary>
    ///     Flushes pending messages and closes the producer.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _producer.Flush(KafkaProducerFactory.FlushTimeout);
        }
        finally
        {
            _producer.Dispose();
        }
    }
}
