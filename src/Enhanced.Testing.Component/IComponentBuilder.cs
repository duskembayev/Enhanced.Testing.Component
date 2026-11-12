namespace Enhanced.Testing.Component;

/// <summary>
///     Represents a component builder.
/// </summary>
public interface IComponentBuilder
{
    /// <summary>
    ///     Adds a dependency to the component.
    /// </summary>
    /// <param name="dependency">
    ///     The dependency to add.
    /// </param>
    /// <returns>
    ///     The component builder.
    /// </returns>
    IComponentBuilder AddDependency(IComponentDependency dependency);

    /// <summary>
    ///     Configures the web host of the application under test.
    /// </summary>
    /// <param name="configuration">
    ///     The web host builder configuration.
    /// </param>
    /// <returns>
    ///     The component builder.
    /// </returns>
    IComponentBuilder Configure(Action<IWebHostBuilder> configuration);

    /// <summary>
    ///     Builds the component. A builder can build only one component.
    /// </summary>
    /// <returns>
    ///     The instance of the <see cref="IComponent" />.
    /// </returns>
    IComponent Build();
}
