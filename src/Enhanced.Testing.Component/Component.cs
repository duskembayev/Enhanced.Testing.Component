using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.TestHost;

namespace Enhanced.Testing.Component;

internal sealed class Component<TEntryPoint> : IComponent where TEntryPoint : class
{
    private readonly WebApplicationFactory<TEntryPoint> _appFactory;
    private readonly IReadOnlyList<IComponentDependency> _dependencies;
    private readonly bool[] _startedDependencies;
    private bool _appDisposed;
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

        for (var i = 0; i < _dependencies.Count; i++)
        {
            if (!IsContainerHarness(_dependencies[i]))
            {
                continue;
            }

            var dependency = _dependencies[i];
            _startedDependencies[i] = true;
            starts.Add(Task.Run(() => dependency.OnStartAsync(this, cancellationToken), CancellationToken.None));
        }

        if (starts.Count == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(starts).ConfigureAwait(false);
        }
        catch
        {
            var exceptions = starts.Where(start => start.IsFaulted)
                                   .SelectMany(start => start.Exception!.InnerExceptions)
                                   .ToList();

            if (exceptions.Count == 0)
            {
                throw;
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
    }

    private static bool IsCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    private static bool IsContainerHarness(IComponentDependency dependency)
    {
        for (var type = dependency.GetType(); type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ContainerHarness<>))
            {
                return true;
            }
        }

        return false;
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
