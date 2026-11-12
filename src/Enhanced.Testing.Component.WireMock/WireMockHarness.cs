using Microsoft.AspNetCore.Hosting;
using WireMock.Server;
using WireMock.Settings;

namespace Enhanced.Testing.Component.WireMock;

/// <summary>
///     A harness that hosts an in-process WireMock.Net server to fake external HTTP dependencies.
/// </summary>
public class WireMockHarness : Harness
{
    private WireMockServer? _server;

    /// <summary>
    ///     The configuration key that receives the server base URL, for example <c>Services:Payments:BaseUrl</c>.
    /// </summary>
    public string? ConfigurationKey { get; init; }

    /// <summary>
    ///     Customizes the server settings before the server is started.
    /// </summary>
    public Action<WireMockServerSettings>? ConfigureServer { get; init; }

    /// <summary>
    ///     The WireMock.Net server used to stub requests and verify received calls.
    /// </summary>
    public WireMockServer Server
    {
        get
        {
            ThrowIfComponentNotStarted();

            if (_server is null)
            {
                throw new InvalidOperationException("The server is not running.");
            }

            return _server;
        }
    }

    /// <summary>
    ///     The base URL of the server.
    /// </summary>
    public Uri BaseAddress => new(Server.Url!);

    /// <summary>
    ///     Creates an <see cref="HttpClient" /> that calls the server.
    /// </summary>
    /// <returns>
    ///     The HTTP client.
    /// </returns>
    public HttpClient CreateClient() => new() { BaseAddress = BaseAddress };

    /// <summary>
    ///     Removes all stubs and clears the request log.
    /// </summary>
    public void Reset() => Server.Reset();

    /// <inheritdoc />
    public override void OnConfigure(IWebHostBuilder builder)
    {
        if (ConfigurationKey is null)
        {
            return;
        }

        builder.UseSetting(ConfigurationKey, Server.Url);
    }

    /// <inheritdoc />
    protected override Task OnStart(CancellationToken cancellationToken)
    {
        var settings = new WireMockServerSettings { StartAdminInterface = false };
        ConfigureServer?.Invoke(settings);
        _server = WireMockServer.Start(settings);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task OnStop(CancellationToken cancellationToken)
    {
        if (_server is null)
        {
            return Task.CompletedTask;
        }

        _server.Stop();
        _server.Dispose();
        _server = null;
        return Task.CompletedTask;
    }
}
