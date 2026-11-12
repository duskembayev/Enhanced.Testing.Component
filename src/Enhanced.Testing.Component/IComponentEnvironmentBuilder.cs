namespace Enhanced.Testing.Component;

/// <summary>
///     Represents a component environment builder.
/// </summary>
public interface IComponentEnvironmentBuilder
{
    /// <summary>
    ///     Adds a harness that is started once with the environment and shared by all of its components.
    /// </summary>
    /// <param name="harness">
    ///     The harness.
    /// </param>
    /// <returns>
    ///     The environment builder.
    /// </returns>
    IComponentEnvironmentBuilder AddHarness(IEnvironmentHarness harness);

    /// <summary>
    ///     Configures the web host builder of every component created from the environment.
    /// </summary>
    /// <param name="configuration">
    ///     The configuration action.
    /// </param>
    /// <returns>
    ///     The environment builder.
    /// </returns>
    IComponentEnvironmentBuilder Configure(Action<IWebHostBuilder> configuration);

    /// <summary>
    ///     Starts the harnesses in order. If starting fails, the harnesses started so far are stopped before the
    ///     exception is rethrown.
    /// </summary>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    /// <returns>
    ///     The started environment.
    /// </returns>
    Task<IComponentEnvironment> StartAsync(CancellationToken cancellationToken = default);
}
