using Microsoft.AspNetCore.TestHost;

namespace Enhanced.Testing.Component;

internal sealed class Component<TEntryPoint> : IComponent where TEntryPoint : class
{
    private readonly WebApplicationFactory<TEntryPoint> _appFactory;
    private readonly IReadOnlyList<IComponentDependency> _dependencies;
    private bool _started;

    public Component(WebApplicationFactory<TEntryPoint> appFactory, IReadOnlyList<IComponentDependency> dependencies)
    {
        _appFactory = appFactory;
        _dependencies = dependencies;
    }

    public WebApplicationFactoryClientOptions ClientOptions => _appFactory.ClientOptions;

    public TestServer Server
    {
        get
        {
            ThrowIfComponentNotStarted();
            return _appFactory.Server;
        }
    }

    public IServiceProvider Services
    {
        get
        {
            ThrowIfComponentNotStarted();
            return _appFactory.Services;
        }
    }

    public HttpClient CreateClient()
    {
        ThrowIfComponentNotStarted();
        return _appFactory.CreateClient();
    }

    public HttpClient CreateClient(WebApplicationFactoryClientOptions options)
    {
        ThrowIfComponentNotStarted();
        return _appFactory.CreateClient(options);
    }

    public HttpClient CreateDefaultClient(params DelegatingHandler[] handlers)
    {
        ThrowIfComponentNotStarted();
        return _appFactory.CreateDefaultClient(handlers);
    }

    public HttpClient CreateDefaultClient(Uri baseAddress, params DelegatingHandler[] handlers)
    {
        ThrowIfComponentNotStarted();
        return _appFactory.CreateDefaultClient(baseAddress, handlers);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < _dependencies.Count; i++)
        {
            await _dependencies[i].OnStartAsync(this, cancellationToken).ConfigureAwait(false);
        }

        _ = _appFactory.Server;
        _started = true;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        for (var i = _dependencies.Count - 1; i >= 0; i--)
        {
            await _dependencies[i].OnStopAsync(this, cancellationToken).ConfigureAwait(false);
        }

        await _appFactory.DisposeAsync().ConfigureAwait(false);
        _started = false;
    }

    private void ThrowIfComponentNotStarted()
    {
        if (!_started)
        {
            throw new InvalidOperationException("The component has not been started.");
        }
    }
}
