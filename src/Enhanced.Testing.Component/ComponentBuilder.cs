namespace Enhanced.Testing.Component;

internal sealed class ComponentBuilder<TEntryPoint> : IComponentBuilder where TEntryPoint : class
{
    private readonly List<IComponentDependency> _dependencies = new();
    private readonly RunningComponentEnvironment? _environment;
    private WebApplicationFactory<TEntryPoint> _appFactory;
    private bool _built;

    public ComponentBuilder(WebApplicationFactory<TEntryPoint> appFactory, RunningComponentEnvironment? environment = null)
    {
        _appFactory = appFactory;
        _environment = environment;
    }

    public IComponentBuilder AddDependency(IComponentDependency dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        _dependencies.Add(dependency);
        return this;
    }

    public IComponentBuilder Configure(Action<IWebHostBuilder> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _appFactory = _appFactory.WithWebHostBuilder(configuration);
        return this;
    }

    public IComponent Build()
    {
        if (_built)
        {
            throw new InvalidOperationException("The component has already been built. Create a new builder instead.");
        }

        _built = true;
        return new Component<TEntryPoint>(_appFactory, _dependencies.ToArray(), _environment);
    }
}
