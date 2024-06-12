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
    ///     Configures the component.
    /// </summary>
    /// <param name="configuration"></param>
    /// <returns></returns>
    IComponentBuilder Configure(Action<IWebHostBuilder> configuration);

    /// <summary>
    ///     Builds the component.
    /// </summary>
    /// <returns>
    ///     The instance of the <see cref="IComponent" />.
    /// </returns>
    IComponent Build();
}
