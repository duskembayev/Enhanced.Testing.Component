namespace Enhanced.Testing.Component;

/// <summary>
///     Provides a way to create a component builder.
/// </summary>
public static class ComponentBuilder
{
    /// <summary>
    ///     Creates a new component builder.
    /// </summary>
    /// <typeparam name="TEntryPoint">
    ///     A type in the entry point assembly of the application. Typically the Startup or Program classes can be used.
    /// </typeparam>
    /// <returns>
    ///     The component builder instance.
    /// </returns>
    public static IComponentBuilder Create<TEntryPoint>()
        where TEntryPoint : class
    {
        WebApplicationFactory<TEntryPoint> appFactory = new();
        return new ComponentBuilder<TEntryPoint>(appFactory);
    }

    /// <summary>
    ///     Creates a new component builder that hosts the application through the specified factory, for example a
    ///     subclass that overrides <c>CreateHost</c> or <c>ConfigureWebHost</c>.
    ///     The component does not dispose <paramref name="appFactory" />; the caller remains its owner.
    /// </summary>
    /// <typeparam name="TEntryPoint">
    ///     A type in the entry point assembly of the application. Typically the Startup or Program classes can be used.
    /// </typeparam>
    /// <param name="appFactory">
    ///     The application factory.
    /// </param>
    /// <returns>
    ///     The component builder instance.
    /// </returns>
    public static IComponentBuilder Create<TEntryPoint>(WebApplicationFactory<TEntryPoint> appFactory)
        where TEntryPoint : class
    {
        ArgumentNullException.ThrowIfNull(appFactory);
        return new ComponentBuilder<TEntryPoint>(appFactory.WithWebHostBuilder(_ => { }));
    }
}
