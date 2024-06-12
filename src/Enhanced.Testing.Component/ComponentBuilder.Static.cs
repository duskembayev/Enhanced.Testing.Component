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
}
