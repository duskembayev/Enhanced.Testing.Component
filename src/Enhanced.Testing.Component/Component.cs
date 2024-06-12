namespace Enhanced.Testing.Component;

internal sealed class Component<TEntryPoint> : IComponent where TEntryPoint : class
{
    private readonly WebApplicationFactory<TEntryPoint> _appFactory;
    private readonly IReadOnlyList<IComponentDependency> _dependencies;

    public Component(WebApplicationFactory<TEntryPoint> appFactory, IReadOnlyList<IComponentDependency> dependencies)
    {
        _appFactory = appFactory;
        _dependencies = dependencies;
    }

    public IServiceProvider Services => _appFactory.Services;
    public WebApplicationFactoryClientOptions ClientOptions => _appFactory.ClientOptions;

    public HttpClient CreateClient() => _appFactory.CreateClient();

    public HttpClient CreateClient(WebApplicationFactoryClientOptions options) => _appFactory.CreateClient(options);

    public HttpClient CreateDefaultClient(params DelegatingHandler[] handlers) =>
        _appFactory.CreateDefaultClient(handlers);

    public HttpClient CreateDefaultClient(Uri baseAddress, params DelegatingHandler[] handlers) =>
        _appFactory.CreateDefaultClient(baseAddress, handlers);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < _dependencies.Count; i++)
        {
            await _dependencies[i].OnStartAsync(this, cancellationToken);
        }

        _ = _appFactory.Server;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        for (var i = _dependencies.Count - 1; i >= 0; i--)
        {
            await _dependencies[i].OnStopAsync(this, cancellationToken);
        }

        await _appFactory.DisposeAsync();
    }
}
