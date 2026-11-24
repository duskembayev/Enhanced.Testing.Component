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
    private readonly string _endpointName = "http";
    private readonly string? _serviceName;
    private bool _explainsGrpcCalls;
    private WireMockServer? _server;

    /// <summary>
    ///     The configuration key that receives the server base URL, for example <c>Services:Payments:BaseUrl</c>.
    ///     If <see langword="null" />, the URL is not added to the configuration.
    /// </summary>
    public string? ConfigurationKey { get; init; }

    /// <summary>
    ///     The name under which the application finds the server through service discovery
    ///     (<c>Microsoft.Extensions.ServiceDiscovery</c>), for example <c>payments</c> for a client that calls
    ///     <c>http://payments</c>. The server address is passed to the application as
    ///     <c>Services:{ServiceName}:{EndpointName}:0</c>. If <see langword="null" />, the server is not registered
    ///     for service discovery.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     Thrown if the value is empty.
    /// </exception>
    public string? ServiceName
    {
        get => _serviceName;
        init
        {
            if (value is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(ServiceName));
            }

            _serviceName = value;
        }
    }

    /// <summary>
    ///     The endpoint of the service the server stands for. The default, <c>http</c>, is the endpoint a client
    ///     resolves for an address without an endpoint name, such as <c>http://payments</c> or
    ///     <c>https+http://payments</c>. Set it for a named endpoint: a client that calls
    ///     <c>http://_grpc.payments</c> resolves the endpoint <c>grpc</c>.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     Thrown if the value is empty.
    /// </exception>
    public string EndpointName
    {
        get => _endpointName;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(EndpointName));
            _endpointName = value;
        }
    }

    /// <summary>
    ///     Makes the server listen over HTTP/2 instead of HTTP/1.1, which gRPC needs. Such a server does not answer
    ///     HTTP/1.1 requests: use a separate harness for the HTTP APIs the application calls.
    /// </summary>
    public bool UseHttp2 { get; init; }

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
    public void Reset()
    {
        Server.Reset();
        ExplainUnmatchedGrpcCalls();
    }

    internal bool ListensOverHttp2 => _explainsGrpcCalls;

    private void ExplainUnmatchedGrpcCalls()
    {
        if (_explainsGrpcCalls && _server is not null)
        {
            GrpcUnmatchedCalls.Explain(_server);
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    ///     Thrown if <see cref="ConfigurationKey" /> is set and the server is not running.
    /// </exception>
    public override void OnConfigure(IWebHostBuilder builder)
    {
        var serviceKey = ServiceName is null ? null : $"Services:{ServiceName}:{EndpointName}:0";

        if (ConfigurationKey is null && serviceKey is null)
        {
            return;
        }

        var url = _server?.Url ?? throw new InvalidOperationException(
            "The WireMock server must be started before the web host is configured with " +
            $"'{ConfigurationKey ?? serviceKey}'.");

        if (ConfigurationKey is not null)
        {
            builder.UseSetting(ConfigurationKey, url);
        }

        if (serviceKey is not null)
        {
            builder.UseSetting(serviceKey, url);
        }
    }

    /// <inheritdoc />
    protected override Task OnStart(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = new WireMockServerSettings { StartAdminInterface = false, UseHttp2 = UseHttp2 };
        ConfigureServer?.Invoke(settings);
        _server = WireMockServer.Start(settings);
        _explainsGrpcCalls = settings.UseHttp2 == true;
        ExplainUnmatchedGrpcCalls();
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
