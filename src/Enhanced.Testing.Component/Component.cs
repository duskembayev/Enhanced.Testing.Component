using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Enhanced.Testing.Component;

internal sealed class Component<TEntryPoint> : IComponent where TEntryPoint : class
{
    private readonly WebApplicationFactory<TEntryPoint> _appFactory;
    private readonly IReadOnlyList<IComponentDependency> _dependencies;
    private readonly bool[] _startedDependencies;
    private bool _appDisposed;
    private ApplicationStop? _applicationStop;
    private CancellationTokenRegistration _applicationStopping;
    private ComponentState _state;

    public Component(WebApplicationFactory<TEntryPoint> appFactory, IReadOnlyList<IComponentDependency> dependencies)
    {
        _appFactory = appFactory;
        _dependencies = dependencies;
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

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_state != ComponentState.Created)
        {
            throw new InvalidOperationException("The component can be started only once.");
        }

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
            WatchApplicationStop();
        }
        catch (Exception startException)
        {
            try
            {
                await StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception stopException)
            {
                throw new AggregateException(
                    "The component failed to start and could not be stopped cleanly.",
                    startException,
                    stopException);
            }

            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_state == ComponentState.Stopped)
        {
            return;
        }

        _state = ComponentState.Stopping;
        _applicationStopping.Dispose();
        _applicationStopping = default;

        List<Exception>? exceptions = null;
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
                    (exceptions ??= []).Add(e);
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
                (exceptions ??= []).Add(e);
            }
        }

        if (exceptions is { Count: 1 })
        {
            ExceptionDispatchInfo.Throw(exceptions[0]);
        }

        if (exceptions is not null)
        {
            throw new AggregateException("One or more errors occurred while stopping the component.", exceptions);
        }

        if (cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        _state = ComponentState.Stopped;
    }

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

        List<Exception>? exceptions = null;

        foreach (var start in starts)
        {
            try
            {
                await start.ConfigureAwait(false);
            }
            catch (Exception e)
            {
                (exceptions ??= []).Add(e);
            }
        }

        if (exceptions is null)
        {
            return;
        }

        var failures = exceptions.Where(e => !IsCancellation(e, cancellationToken)).ToList();

        if (failures.Count == 0)
        {
            ExceptionDispatchInfo.Throw(exceptions[0]);
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Throw(failures[0]);
        }

        throw new AggregateException("One or more container harnesses failed to start.", failures);
    }

    private static bool IsCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    private void WatchApplicationStop()
    {
        var lifetime = _appFactory.Services.GetService<IHostApplicationLifetime>();

        if (lifetime is not null)
        {
            _applicationStopping = lifetime.ApplicationStopping.Register(OnApplicationStopping);
        }
    }

    private void OnApplicationStopping()
    {
        string? service = null;
        Exception? failure = null;

        try
        {
            foreach (var hostedService in _appFactory.Services.GetServices<IHostedService>())
            {
                if (hostedService is BackgroundService { ExecuteTask: { IsFaulted: true } task })
                {
                    service = hostedService.GetType().Name;
                    failure = task.Exception.InnerExceptions.Count == 1 ? task.Exception.InnerException : task.Exception;
                    break;
                }
            }
        }
        catch (Exception)
        {
            service = null;
            failure = null;
        }

        Volatile.Write(ref _applicationStop, new ApplicationStop(service, failure));
    }

    private void ThrowIfComponentNotStarted()
    {
        switch (_state)
        {
            case ComponentState.Started:
                Volatile.Read(ref _applicationStop)?.Throw();
                return;
            case ComponentState.Stopping:
            case ComponentState.Stopped:
                throw new InvalidOperationException("The component has been stopped.");
            default:
                throw new InvalidOperationException("The component has not been started.");
        }
    }

    private sealed class ApplicationStop(string? service, Exception? failure)
    {
        public void Throw()
        {
            if (failure is null)
            {
                throw new InvalidOperationException(
                    "The application stopped while the component was running, and the component did not stop it. " +
                    "See the application's log for the reason.");
            }

            throw new InvalidOperationException(
                $"The application stopped while the component was running: background service '{service}' failed " +
                $"with {failure.GetType().Name}: {failure.Message}",
                failure);
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
