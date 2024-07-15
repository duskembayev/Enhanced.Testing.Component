using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Testcontainers.Kafka;

namespace Enhanced.Testing.Component.Kafka;

/// <summary>
///     A Kafka harness.
/// </summary>
public class KafkaHarness : Harness
{
    private KafkaContainer? _container;

    /// <summary>
    ///     The topics to create.
    /// </summary>
    public IList<string> Topics { get; set; } = [];

    /// <summary>
    ///     The Kafka container.
    /// </summary>
    public KafkaContainer Container
    {
        get
        {
            ThrowIfComponentNotStarted();
            return _container!;
        }
    }

    /// <summary>
    ///     The name of the connection string to add to the configuration.
    /// </summary>
    public string? ConnectionStringName { get; set; }

    /// <summary>
    ///     Returns the broker address.
    /// </summary>
    /// <returns></returns>
    public string GetBootstrapAddress()
    {
        return Container.GetBootstrapAddress();
    }

    /// <inheritdoc />
    public override void OnConfigure(IWebHostBuilder webHostBuilder)
    {
        if (ConnectionStringName is null)
        {
            return;
        }

        webHostBuilder.ConfigureAppConfiguration(
            builder => builder.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:" + ConnectionStringName] = GetBootstrapAddress()
                }));
    }

    /// <inheritdoc />
    protected override async Task OnStart(CancellationToken cancellationToken)
    {
        _container = new KafkaBuilder().Build();
        await _container.StartAsync(cancellationToken).ConfigureAwait(false);

        foreach (var topic in Topics)
        {
            await CreateTopicAsync(topic, cancellationToken).ConfigureAwait(false);
        }
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
        string[] command =
        [
            "kafka-topics",
            $"--bootstrap-server localhost:{KafkaBuilder.BrokerPort}",
            $"--topic {topicName}",
            "--create",
            "--partitions 1",
            "--replication-factor 1"
        ];

        var result = await Container.ExecAsync(command, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    /// <inheritdoc />
    protected override async Task OnStop(CancellationToken cancellationToken)
    {
        if (_container is not null)
        {
            await _container.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}