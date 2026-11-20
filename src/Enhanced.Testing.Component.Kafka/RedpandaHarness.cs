using Testcontainers.Redpanda;

namespace Enhanced.Testing.Component.Kafka;

/// <summary>
///     A Redpanda harness that provides a Kafka-compatible broker.
/// </summary>
public class RedpandaHarness : KafkaBrokerHarness<RedpandaContainer>
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
}
