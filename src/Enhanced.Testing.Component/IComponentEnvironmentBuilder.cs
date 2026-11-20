namespace Enhanced.Testing.Component;

/// <summary>
///     Represents a component environment builder.
/// </summary>
public interface IComponentEnvironmentBuilder
{
    /// <summary>
    ///     Adds a harness that is started once with the environment and shared by all of its components. Its
    ///     configuration is applied to every component in the order of the <c>AddHarness</c> and <c>Configure</c> calls.
    /// </summary>
    /// <param name="harness">
    ///     The harness.
    /// </param>
    /// <returns>
    ///     The environment builder.
    /// </returns>
    IComponentEnvironmentBuilder AddHarness(IEnvironmentHarness harness);

    /// <summary>
    ///     Configures the web host builder of every component created from the environment. Configuration is applied
    ///     in the order of the <c>AddHarness</c> and <c>Configure</c> calls, before the component's own configuration.
    /// </summary>
    /// <param name="configuration">
    ///     The configuration action.
    /// </param>
    /// <returns>
    ///     The environment builder.
    /// </returns>
    IComponentEnvironmentBuilder Configure(Action<IWebHostBuilder> configuration);

    /// <summary>
    ///     Starts the container harnesses concurrently (except those with
    ///     <see cref="ContainerHarness{TContainer}.StartConcurrently" /> set to <see langword="false" />), then the
    ///     other harnesses in the order they were added. If starting fails, the harnesses started so far are stopped
    ///     before the exception is rethrown.
    /// </summary>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    /// <returns>
    ///     The started environment.
    /// </returns>
    Task<IComponentEnvironment> StartAsync(CancellationToken cancellationToken = default);
}
