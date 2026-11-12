namespace Enhanced.Testing.Component;

/// <summary>
///     Provides a way to create a component environment.
/// </summary>
public static class ComponentEnvironment
{
    /// <summary>
    ///     Creates a new component environment builder.
    /// </summary>
    /// <returns>
    ///     The component environment builder.
    /// </returns>
    public static IComponentEnvironmentBuilder Create() => new ComponentEnvironmentBuilder();
}
