using Microsoft.AspNetCore.Hosting;
using WireMock.Server;
using WireMock.Settings;

namespace Enhanced.Testing.Component.WireMock;

/// <summary>
///     A harness that hosts an in-process WireMock.Net server to fake external HTTP dependencies.
/// </summary>
/// <remarks>
///     The server starts when the component starts and stops when the component stops. The harness can be added to
///     another component after the previous one has stopped; each start creates a new server.
/// </remarks>
public class WireMockHarness : Harness
{
    private WireMockServer? _server;

    /// <summary>
    ///     The configuration key that receives the server base URL, for example <c>Services:Payments:BaseUrl</c>.
    ///     If <see langword="null" />, the URL is not added to the configuration.
    /// </summary>
    public string? ConfigurationKey { get; init; }

    /// <summary>
    ///     Customizes the server settings before the server is started. By default, the server listens over HTTP on a
    ///     free dynamic port and the admin interface is disabled.
    /// </summary>
    public Action<WireMockServerSettings>? ConfigureServer { get; init; }

    /// <summary>
    ///     The WireMock.Net server used to stub requests and verify received calls.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the component is not started or the server is not running.
    /// </exception>
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
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the component is not started or the server is not running.
    /// </exception>
    public Uri BaseAddress => new(Server.Url ?? throw new InvalidOperationException("The server has no URL."));

    /// <summary>
    ///     Creates an <see cref="HttpClient" /> that calls the server. The caller is responsible for disposing it.
    /// </summary>
    /// <returns>
    ///     The HTTP client with <see cref="HttpClient.BaseAddress" /> set to <see cref="BaseAddress" />.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the component is not started or the server is not running.
    /// </exception>
    public HttpClient CreateClient() => new() { BaseAddress = BaseAddress };

    /// <summary>
    ///     Removes all stubs, clears the request log and resets scenario states. Mappings created from the server
    ///     settings (for example, proxy or static mappings set up in <see cref="ConfigureServer" />) are removed too.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the component is not started or the server is not running.
    /// </exception>
    public void Reset() => Server.Reset();

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    ///     Thrown if <see cref="ConfigurationKey" /> is set and the server is not running.
    /// </exception>
    public override void OnConfigure(IWebHostBuilder builder)
    {
        if (ConfigurationKey is null)
        {
            return;
        }

        var url = _server?.Url ?? throw new InvalidOperationException(
            $"The WireMock server must be started before the web host is configured with '{ConfigurationKey}'.");

        builder.UseSetting(ConfigurationKey, url);
    }

    /// <inheritdoc />
    protected override Task OnStart(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = new WireMockServerSettings { StartAdminInterface = false };
        ConfigureServer?.Invoke(settings);
        _server = WireMockServer.Start(settings);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task OnStop(CancellationToken cancellationToken)
    {
        var server = _server;

        if (server is null)
        {
            return Task.CompletedTask;
        }

        _server = null;

        try
        {
            server.Stop();
        }
        finally
        {
            server.Dispose();
        }

        return Task.CompletedTask;
    }
}
