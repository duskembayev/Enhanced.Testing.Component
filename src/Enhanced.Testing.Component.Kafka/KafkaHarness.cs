using Testcontainers.Kafka;

namespace Enhanced.Testing.Component.Kafka;

/// <summary>
///     A Kafka harness.
/// </summary>
/// <remarks>
///     Confluent (<c>confluentinc/cp-kafka</c>) and Apache (<c>apache/kafka</c>, <c>apache/kafka-native</c>) images are
///     supported; the vendor is detected from the image name.
/// </remarks>
public class KafkaHarness : KafkaBrokerHarness<KafkaContainer>
{
    /// <summary>
    ///     The Apache Kafka GraalVM native image. It starts faster and uses less memory than the JVM images.
    /// </summary>
    public const string NativeImage = "apache/kafka-native:4.4.0";

    /// <summary>
    ///     The Confluent Platform Kafka image.
    /// </summary>
    public const string ConfluentImage = "confluentinc/cp-kafka:7.6.1";

    /// <summary>
    ///     The default container image.
    /// </summary>
    public const string DefaultImage = NativeImage;

    /// <summary>
    ///     The container image.
    /// </summary>
    public string Image { get; init; } = DefaultImage;

    /// <summary>
    ///     Customizes the container builder before the container is built.
    /// </summary>
    public Func<KafkaBuilder, KafkaBuilder>? ConfigureContainer { get; init; }

    /// <inheritdoc cref="IKafkaHarness.GetConnectionString" />
    public override string GetConnectionString()
    {
        return $"{Container.Hostname}:{Container.GetMappedPublicPort(KafkaBuilder.KafkaPort)}";
    }

    /// <inheritdoc />
    protected override KafkaContainer OnCreateContainer()
    {
        var builder = new KafkaBuilder(Image);
        return (ConfigureContainer?.Invoke(builder) ?? builder).Build();
    }
}
