using System.Diagnostics;
using Testcontainers.Kafka;

namespace Enhanced.Testing.Component.Kafka;

/// <summary>
///     A Kafka harness.
/// </summary>
public class KafkaHarness : ContainerHarness<KafkaContainer>
{
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
            await CreateTopicAsync(topic, cancellationToken).ConfigureAwait(false);
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
        return new KafkaBuilder().Build();
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
        string[] cmd = ["kafka-topics", "--create", "--bootstrap-server", "127.0.0.1:9093", "--topic", topicName];
        var result = await Container.ExecAsync(cmd, cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            Debug.Fail($"Failed to create topic {topicName}: {result.Stderr}");
            return false;
        }

        Debug.WriteLine(result.Stdout);
        return result.ExitCode == 0;
    }
}
