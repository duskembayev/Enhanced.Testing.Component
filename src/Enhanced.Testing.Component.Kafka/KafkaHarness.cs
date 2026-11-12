using DotNet.Testcontainers.Containers;
using Testcontainers.Kafka;

namespace Enhanced.Testing.Component.Kafka;

/// <summary>
///     A Kafka harness.
/// </summary>
public class KafkaHarness : ContainerHarness<KafkaContainer>
{
    /// <summary>
    ///     The default container image.
    /// </summary>
    public const string DefaultImage = "confluentinc/cp-kafka:7.6.1";

    /// <summary>
    ///     The container image.
    /// </summary>
    public string Image { get; init; } = DefaultImage;

    /// <summary>
    ///     Customizes the container builder before the container is built.
    /// </summary>
    public Func<KafkaBuilder, KafkaBuilder>? ConfigureContainer { get; init; }

    /// <summary>
    ///     The topics to create.
    /// </summary>
    public IList<string> Topics { get; init; } = [];

    /// <inheritdoc />
    protected override async Task OnStart(CancellationToken cancellationToken)
    {
        await base.OnStart(cancellationToken).ConfigureAwait(false);

        foreach (var topic in Topics)
        {
            var result = await ExecCreateTopicAsync(topic, cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to create Kafka topic '{topic}' (exit code {result.ExitCode}): {result.Stderr.Trim()}");
            }
        }
    }

    /// <inheritdoc />
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

    /// <summary>
    ///     Creates topics.
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
        var result = await ExecCreateTopicAsync(topicName, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    private Task<ExecResult> ExecCreateTopicAsync(string topicName, CancellationToken cancellationToken)
    {
        string[] cmd =
        [
            "kafka-topics", "--create", "--bootstrap-server", $"127.0.0.1:{KafkaBuilder.BrokerPort}", "--topic", topicName
        ];

        return Container.ExecAsync(cmd, cancellationToken);
    }
}
