using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Testcontainers.Redis;

namespace Enhanced.Testing.Component.Redis;

/// <summary>
/// A Redis harness.
/// </summary>
public class RedisHarness : Harness
{
    private RedisContainer? _container;

    /// <summary>
    ///     The Redis container.
    /// </summary>
    public RedisContainer Container
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
    ///     Returns the Redis address.
    /// </summary>
    /// <returns></returns>
    public string GetEndpointAddress()
    {
        return $"{Container.Hostname}:{Container.GetMappedPublicPort(RedisBuilder.RedisPort)}";
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
                    ["ConnectionStrings:" + ConnectionStringName] = GetEndpointAddress()
                }));
    }

    /// <inheritdoc />
    protected override async Task OnStart(CancellationToken cancellationToken)
    {
        _container = new RedisBuilder().Build();
        await _container.StartAsync(cancellationToken).ConfigureAwait(false);
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
