using System.Diagnostics;
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
    public IList<string> Topics { get; init; } = [];

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
    public string? ConnectionStringName { get; init; }

    /// <summary>
    ///     Returns the broker address.
    /// </summary>
    /// <returns></returns>
    public string GetBootstrapServer()
    {
        return $"{Container.Hostname}:{Container.GetMappedPublicPort(KafkaBuilder.KafkaPort)}";
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
                    ["ConnectionStrings:" + ConnectionStringName] = GetBootstrapServer()
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

    /// <inheritdoc />
    protected override async Task OnStop(CancellationToken cancellationToken)
    {
        if (_container is not null)
        {
            await _container.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
