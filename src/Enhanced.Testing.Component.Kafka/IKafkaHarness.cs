namespace Enhanced.Testing.Component.Kafka;

/// <summary>
///     A harness that provides a Kafka-compatible broker.
/// </summary>
public interface IKafkaHarness : IHarness
{
    /// <summary>
    ///     Gets the bootstrap servers of the broker.
    /// </summary>
    /// <returns>
    ///     The bootstrap servers in the <c>host:port</c> format.
    /// </returns>
    string GetConnectionString();

    /// <summary>
    ///     Creates a topic and waits until its partitions have a leader.
    /// </summary>
    /// <param name="topicName">
    ///     The topic name.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    /// <returns>
    ///     Returns true if the topic was created; otherwise, false.
    /// </returns>
    Task<bool> CreateTopicAsync(string topicName, CancellationToken cancellationToken = default);
}
