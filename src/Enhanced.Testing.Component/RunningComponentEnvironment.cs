namespace Enhanced.Testing.Component;

internal sealed class RunningComponentEnvironment : IComponentEnvironment
{
    private readonly List<IComponent> _components = [];
    private readonly IReadOnlyList<Action<IWebHostBuilder>> _configurations;
    private readonly IReadOnlyList<IEnvironmentHarness> _harnesses;
    private readonly List<IEnvironmentHarness> _startedHarnesses = [];
    private readonly object _sync = new();
    private EnvironmentState _state;

    public RunningComponentEnvironment(
        IReadOnlyList<Action<IWebHostBuilder>> configurations,
        IReadOnlyList<IEnvironmentHarness> harnesses)
    {
        _configurations = configurations;
        _harnesses = harnesses;
    }

    public IComponentBuilder CreateComponent<TEntryPoint>(Action<IWebHostBuilder>? configure = null)
        where TEntryPoint : class
    {
        ThrowIfNotStarted();

        var appFactory = new WebApplicationFactory<TEntryPoint>().WithWebHostBuilder(builder =>
        {
            foreach (var configuration in _configurations)
            {
                configuration(builder);
            }

            foreach (var harness in _harnesses)
            {
                harness.OnConfigure(builder);
            }

            configure?.Invoke(builder);
        });

        return new ComponentBuilder<TEntryPoint>(appFactory, this);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        List<IComponent> components;

        lock (_sync)
        {
            if (_state == EnvironmentState.Stopped)
            {
                return;
            }

            _state = EnvironmentState.Stopping;
            components = [.. _components];
        }

        List<Exception> exceptions = [];

        var cancelled = await Lifecycle.StopInReverseAsync(
                components,
                component => component.StopAsync(cancellationToken),
                exceptions,
                cancellationToken)
            .ConfigureAwait(false);

        if (!cancelled)
        {
            cancelled = await Lifecycle.StopInReverseAsync(
                    _startedHarnesses,
                    harness => harness.OnStopAsync(this, cancellationToken),
                    exceptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        Lifecycle.ThrowIfFailed(
            exceptions,
            cancelled,
            "One or more errors occurred while stopping the environment.",
            cancellationToken);

        lock (_sync)
        {
            _state = EnvironmentState.Stopped;
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        _state = EnvironmentState.Starting;

        try
        {
            var starts = new List<Task>();

            foreach (var harness in _harnesses)
            {
                if (harness is not IConcurrentStartDependency { StartConcurrently: true } || _startedHarnesses.Contains(harness))
                {
                    continue;
                }

                _startedHarnesses.Add(harness);
                starts.Add(Task.Run(() => harness.OnStartAsync(this, cancellationToken), CancellationToken.None));
            }

            await Lifecycle.WhenAllAsync(starts, "One or more environment harnesses failed to start.", cancellationToken)
                .ConfigureAwait(false);

            foreach (var harness in _harnesses)
            {
                if (_startedHarnesses.Contains(harness))
                {
                    continue;
                }

                _startedHarnesses.Add(harness);
                await harness.OnStartAsync(this, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync)
            {
                _state = EnvironmentState.Started;
            }
        }
        catch (Exception startException)
        {
            await Lifecycle.RollBackAsync(
                    startException,
                    () => StopAsync(CancellationToken.None),
                    "The environment failed to start and could not be stopped cleanly.")
                .ConfigureAwait(false);

            throw;
        }
    }

    internal void Attach(IComponent component)
    {
        lock (_sync)
        {
            ThrowIfNotStarted();
            _components.Add(component);
        }
    }

    internal void Detach(IComponent component)
    {
        lock (_sync)
        {
            _components.Remove(component);
        }
    }

    private void ThrowIfNotStarted()
    {
        if (_state != EnvironmentState.Started)
        {
            throw new InvalidOperationException("The environment is not started.");
        }
    }

    private enum EnvironmentState
    {
        Created,
        Starting,
        Started,
        Stopping,
        Stopped
    }
}
