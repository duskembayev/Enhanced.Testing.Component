namespace Enhanced.Testing.Component;

internal sealed class ComponentBuilder<TEntryPoint> : IComponentBuilder where TEntryPoint : class
{
    private readonly List<IComponentDependency> _dependencies = new();
    private WebApplicationFactory<TEntryPoint> _appFactory;

    public ComponentBuilder(WebApplicationFactory<TEntryPoint> appFactory) => _appFactory = appFactory;

    public IComponentBuilder AddDependency(IComponentDependency dependency)
    {
        _dependencies.Add(dependency);
        return this;
    }

    public IComponentBuilder Configure(Action<IWebHostBuilder> configuration)
    {
        _appFactory = _appFactory.WithWebHostBuilder(configuration);
        return this;
    }

    public IComponent Build() => new Component<TEntryPoint>(_appFactory, _dependencies);
}
