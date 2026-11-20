using DotNet.Testcontainers.Containers;

namespace Enhanced.Testing.Component.Kafka;

/// <summary>
///     The base class of harnesses that run a Kafka-compatible broker in a container.
/// </summary>
/// <remarks>
///     Derive from it to plug in another broker: create the container in
///     <see cref="ContainerHarness{TContainer}.OnCreateContainer" /> and return its bootstrap servers from
///     <see cref="GetConnectionString" />.
/// </remarks>
/// <typeparam name="TContainer">
///     The container type.
/// </typeparam>
public abstract class KafkaBrokerHarness<TContainer> : ContainerHarness<TContainer>, IKafkaHarness
    where TContainer : IContainer
{
    /// <summary>
    ///     The topics to create on start. Topics that already exist are left as is.
    /// </summary>
    public IList<string> Topics { get; init; } = [];

    /// <inheritdoc cref="IKafkaHarness.GetConnectionString" />
    public abstract override string GetConnectionString();

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
    public async Task<bool> CreateTopicAsync(string topicName, CancellationToken cancellationToken = default)
    {
        return await KafkaTopicAdmin.TryCreateTopicAsync(GetConnectionString(), topicName, cancellationToken)
                                    .ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task OnStart(CancellationToken cancellationToken)
    {
        await base.OnStart(cancellationToken).ConfigureAwait(false);

        foreach (var topic in Topics)
        {
            await KafkaTopicAdmin.EnsureTopicAsync(GetConnectionString(), topic, cancellationToken)
                                 .ConfigureAwait(false);
        }
    }
}
