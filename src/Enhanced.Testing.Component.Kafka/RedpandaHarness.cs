using Testcontainers.Redpanda;

namespace Enhanced.Testing.Component.Kafka;

/// <summary>
///     A Redpanda harness that provides a Kafka-compatible broker.
/// </summary>
public class RedpandaHarness : ContainerHarness<RedpandaContainer>, IKafkaHarness
{
    /// <summary>
    ///     The default container image.
    /// </summary>
    public const string DefaultImage = "docker.redpanda.com/redpandadata/redpanda:v26.2.6";

    /// <summary>
    ///     The container image.
    /// </summary>
    public string Image { get; init; } = DefaultImage;

    /// <summary>
    ///     Customizes the container builder before the container is built.
    /// </summary>
    public Func<RedpandaBuilder, RedpandaBuilder>? ConfigureContainer { get; init; }

    /// <summary>
    ///     The topics to create on start. Topics that already exist are left as is.
    /// </summary>
    public IList<string> Topics { get; init; } = [];

    /// <summary>
    ///     The number of partitions of the topics in <see cref="Topics" />. When null, the broker's default is used,
    ///     which is one partition unless the broker is configured otherwise.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown if the value is less than one.
    /// </exception>
    public int? TopicPartitions
    {
        get => _topicPartitions;
        init => _topicPartitions = KafkaTopicAdmin.ValidatePartitions(value, nameof(TopicPartitions));
    }

    private readonly int? _topicPartitions;

    /// <inheritdoc />
    protected override async Task OnStart(CancellationToken cancellationToken)
    {
        await base.OnStart(cancellationToken).ConfigureAwait(false);

        foreach (var topic in Topics)
        {
            await KafkaTopicAdmin.EnsureTopicAsync(GetConnectionString(), topic, TopicPartitions, cancellationToken)
                                 .ConfigureAwait(false);
        }
    }

    /// <inheritdoc cref="IKafkaHarness.GetConnectionString" />
    public override string GetConnectionString()
    {
        return $"{Container.Hostname}:{Container.GetMappedPublicPort(RedpandaBuilder.RedpandaPort)}";
    }

    /// <inheritdoc />
    protected override RedpandaContainer OnCreateContainer()
    {
        var builder = new RedpandaBuilder(Image);
        return (ConfigureContainer?.Invoke(builder) ?? builder).Build();
    }

    /// <inheritdoc />
    public async Task<bool> CreateTopicAsync(string topicName, CancellationToken cancellationToken = default)
    {
        return await KafkaTopicAdmin.TryCreateTopicAsync(GetConnectionString(), topicName, null, cancellationToken)
                                    .ConfigureAwait(false);
    }
}
