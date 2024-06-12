using System.Diagnostics.CodeAnalysis;

namespace Enhanced.Testing.Component;

/// <summary>
///     The base class for a component harness.
/// </summary>
public abstract class Harness : IHarness
{
    private IComponent? _component;

    /// <summary>
    ///     The component.
    /// </summary>
    protected IComponent Component
    {
        get
        {
            ThrowIfComponentNotStarted();
            return _component;
        }
    }

    /// <summary>
    ///     Configures the web host builder.
    /// </summary>
    /// <param name="builder">
    ///     The web host builder.
    /// </param>
    public virtual void OnConfigure(IWebHostBuilder builder)
    {
    }

    Task IComponentDependency.OnStartAsync(IComponent component, CancellationToken cancellationToken)
    {
        if (_component is not null)
        {
            throw new NotSupportedException();
        }

        _component = component;
        return OnStart(cancellationToken);
    }

    Task IComponentDependency.OnStopAsync(IComponent component, CancellationToken cancellationToken) =>
        OnStop(cancellationToken);

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
    ///     Throws an exception if the component is not started.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the component is not started.
    /// </exception>
    [MemberNotNull(nameof(_component))]
    protected void ThrowIfComponentNotStarted()
    {
        if (_component is null)
        {
            throw new InvalidOperationException("Component not started.");
        }
    }
}
