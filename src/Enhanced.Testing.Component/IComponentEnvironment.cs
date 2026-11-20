namespace Enhanced.Testing.Component;

/// <summary>
///     Represents a started environment that owns long-lived harnesses and creates independent components on top of them.
/// </summary>
public interface IComponentEnvironment : IAsyncDisposable
{
    /// <summary>
    ///     Creates a builder for a new component that uses the environment harnesses.
    ///     Each component gets its own application instance; starting and stopping it does not affect the environment
    ///     harnesses.
    /// </summary>
    /// <param name="configure">
    ///     The action to configure the web host builder of the component.
    /// </param>
    /// <typeparam name="TEntryPoint">
    ///     A type in the entry point assembly of the application. Typically the Startup or Program classes can be used.
    /// </typeparam>
    /// <returns>
    ///     The component builder.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the environment is stopped.
    /// </exception>
    IComponentBuilder CreateComponent<TEntryPoint>(Action<IWebHostBuilder>? configure = null)
        where TEntryPoint : class;

    /// <summary>
    ///     Waits for the components created from the environment that are still starting, stops the running ones, then
    ///     stops the environment harnesses in reverse order. A failing step does not prevent the others; calling this
    ///     method again retries the failed steps.
    /// </summary>
    /// <param name="cancellationToken">
    ///     The cancellation token. Cancellation is observed between steps.
    /// </param>
    Task StopAsync(CancellationToken cancellationToken = default);
}
