namespace Enhanced.Testing.Component;

/// <summary>
///     The base class for a component harness.
/// </summary>
public abstract class Harness : IHarness
{
    private IComponent? _component;
    private object? _owner;

    /// <summary>
    ///     The component.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the harness is not started or is started by an environment rather than a component.
    /// </exception>
    protected IComponent Component
    {
        get
        {
            ThrowIfComponentNotStarted();
            return _component ?? throw new InvalidOperationException(
                "The harness is started by an environment and is not bound to a component.");
        }
    }

    /// <summary>
    ///     Whether the harness is started by a component or an environment.
    /// </summary>
    public bool IsStarted => _owner is not null;

    /// <summary>
    ///     Configures the web host builder.
    /// </summary>
    /// <param name="builder">
    ///     The web host builder.
    /// </param>
    public virtual void OnConfigure(IWebHostBuilder builder)
    {
    }

    Task IComponentDependency.OnStartAsync(IComponent component, CancellationToken cancellationToken) =>
        AttachAsync(component, component, cancellationToken);

    Task IComponentDependency.OnStopAsync(IComponent component, CancellationToken cancellationToken) =>
        DetachAsync(component, cancellationToken);

    private protected Task AttachAsync(object owner, IComponent? component, CancellationToken cancellationToken)
    {
        if (_owner is not null)
        {
            throw new NotSupportedException(
                "The harness is already started by another component or environment. " +
                "Use a separate harness instance or stop the previous owner first.");
        }

        _owner = owner;
        _component = component;
        return OnStart(cancellationToken);
    }

    private protected async Task DetachAsync(object owner, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(_owner, owner))
        {
            return;
        }

        await OnStop(cancellationToken).ConfigureAwait(false);
        _owner = null;
        _component = null;
    }

    /// <summary>
    ///     Called when the component is starting.
    /// </summary>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    protected virtual Task OnStart(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    ///     Called when the component is stopping.
    /// </summary>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    protected virtual Task OnStop(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    ///     Throws an exception if the harness is not started or has been stopped.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the component is not started.
    /// </exception>
    protected void ThrowIfComponentNotStarted()
    {
        if (_owner is null)
        {
            throw new InvalidOperationException("The harness is not started.");
        }
    }
}
