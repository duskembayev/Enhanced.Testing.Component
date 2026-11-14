namespace Enhanced.Testing.Component;

internal sealed class ComponentEnvironmentBuilder : IComponentEnvironmentBuilder
{
    private readonly List<Action<IWebHostBuilder>> _configurations = [];
    private readonly List<IEnvironmentHarness> _harnesses = [];

    public IComponentEnvironmentBuilder AddHarness(IEnvironmentHarness harness)
    {
        ArgumentNullException.ThrowIfNull(harness);
        _harnesses.Add(harness);
        _configurations.Add(harness.OnConfigure);
        return this;
    }

    public IComponentEnvironmentBuilder Configure(Action<IWebHostBuilder> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configurations.Add(configuration);
        return this;
    }

    public async Task<IComponentEnvironment> StartAsync(CancellationToken cancellationToken = default)
    {
        var environment = new RunningComponentEnvironment([.. _configurations], [.. _harnesses]);
        await environment.StartAsync(cancellationToken).ConfigureAwait(false);
        return environment;
    }
}
