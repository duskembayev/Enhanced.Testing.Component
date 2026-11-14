namespace Enhanced.Testing.Component;

/// <summary>
///     Represents a harness that is shared by all components created from an <see cref="IComponentEnvironment" />,
///     such as a database or a message broker.
/// </summary>
public interface IEnvironmentHarness
{
    /// <summary>
    ///     Configures the web host builder of every component created from the environment.
    /// </summary>
    /// <param name="builder">
    ///     The web host builder.
    /// </param>
    void OnConfigure(IWebHostBuilder builder);

    /// <summary>
    ///     Called when the environment is started.
    /// </summary>
    /// <param name="environment">
    ///     The environment.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    Task OnStartAsync(IComponentEnvironment environment, CancellationToken cancellationToken);

    /// <summary>
    ///     Called when the environment is stopped, after it has tried to stop its running components. It is called even
    ///     if a component failed to stop.
    /// </summary>
    /// <param name="environment">
    ///     The environment.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    Task OnStopAsync(IComponentEnvironment environment, CancellationToken cancellationToken);
}
