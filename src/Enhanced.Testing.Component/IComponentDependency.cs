namespace Enhanced.Testing.Component;

/// <summary>
///     Represents a component dependency.
/// </summary>
public interface IComponentDependency
{
    /// <summary>
    ///     Called when the component is started.
    /// </summary>
    /// <param name="component">
    ///     The component.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    Task OnStartAsync(IComponent component, CancellationToken cancellationToken);

    /// <summary>
    ///     Called when the component is stopped, after the application has been shut down.
    ///     The application services and test server are no longer available at this point.
    /// </summary>
    /// <param name="component">
    ///     The component.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    /// <returns></returns>
    Task OnStopAsync(IComponent component, CancellationToken cancellationToken);
}
