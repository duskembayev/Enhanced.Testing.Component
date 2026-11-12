using Microsoft.AspNetCore.TestHost;

namespace Enhanced.Testing.Component;

internal sealed class Component<TEntryPoint> : IComponent where TEntryPoint : class
{
    private readonly WebApplicationFactory<TEntryPoint> _appFactory;
    private readonly IReadOnlyList<IComponentDependency> _dependencies;
    private readonly RunningComponentEnvironment? _environment;
    private readonly bool[] _startedDependencies;
    private bool _appDisposed;
    private ComponentState _state;

    public Component(
        WebApplicationFactory<TEntryPoint> appFactory,
        IReadOnlyList<IComponentDependency> dependencies,
        RunningComponentEnvironment? environment)
    {
        _appFactory = appFactory;
        _dependencies = dependencies;
        _environment = environment;
        _startedDependencies = new bool[dependencies.Count];
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

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_state != ComponentState.Created)
        {
            throw new InvalidOperationException("The component can be started only once.");
        }

        _environment?.Attach(this);
        _state = ComponentState.Starting;

        try
        {
            await StartContainersAsync(cancellationToken).ConfigureAwait(false);

            for (var i = 0; i < _dependencies.Count; i++)
            {
                if (_startedDependencies[i])
                {
                    continue;
                }

                _startedDependencies[i] = true;
                await _dependencies[i].OnStartAsync(this, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            _ = _appFactory.Server;
            _state = ComponentState.Started;
        }
        catch (Exception startException)
        {
            await Lifecycle.RollBackAsync(
                    startException,
                    () => StopAsync(CancellationToken.None),
                    "The component failed to start and could not be stopped cleanly.")
                .ConfigureAwait(false);

            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_state == ComponentState.Stopped)
        {
            return;
        }

        _state = ComponentState.Stopping;

        List<Exception> exceptions = [];
        var cancelled = false;

        if (!_appDisposed)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
            }
            else
            {
                try
                {
                    await _appFactory.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    exceptions.Add(e);
                }
                finally
                {
                    _appDisposed = true;
                }
            }
        }

        for (var i = _dependencies.Count - 1; i >= 0 && !cancelled; i--)
        {
            if (!_startedDependencies[i])
            {
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            try
            {
                await _dependencies[i].OnStopAsync(this, cancellationToken).ConfigureAwait(false);
                _startedDependencies[i] = false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
            }
            catch (Exception e)
            {
                exceptions.Add(e);
            }
        }

        Lifecycle.ThrowIfFailed(
            exceptions,
            cancelled,
            "One or more errors occurred while stopping the component.",
            cancellationToken);

        _state = ComponentState.Stopped;
        _environment?.Detach(this);
    }

    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));

    private async Task StartContainersAsync(CancellationToken cancellationToken)
    {
        var starts = new List<Task>();
        var scheduled = new HashSet<IComponentDependency>(ReferenceEqualityComparer.Instance);

        for (var i = 0; i < _dependencies.Count; i++)
        {
            var dependency = _dependencies[i];

            if (dependency is not IConcurrentStartDependency { StartConcurrently: true } || !scheduled.Add(dependency))
            {
                continue;
            }

            _startedDependencies[i] = true;
            starts.Add(Task.Run(() => dependency.OnStartAsync(this, cancellationToken), CancellationToken.None));
        }

        await Lifecycle.WhenAllAsync(starts, "One or more container harnesses failed to start.", cancellationToken)
            .ConfigureAwait(false);
    }

    private void ThrowIfComponentNotStarted()
    {
        switch (_state)
        {
            case ComponentState.Started:
                return;
            case ComponentState.Stopping:
            case ComponentState.Stopped:
                throw new InvalidOperationException("The component has been stopped.");
            default:
                throw new InvalidOperationException("The component has not been started.");
        }
    }

    private enum ComponentState
    {
        Created,
        Starting,
        Started,
        Stopping,
        Stopped
    }
}
