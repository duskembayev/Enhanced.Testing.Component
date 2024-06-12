namespace Enhanced.Testing.Component;

/// <summary>
///     Provides extension methods for <see cref="IComponentBuilder" />.
/// </summary>
public static class ComponentBuilderExtensions
{
    /// <summary>
    ///     Adds a harness to the component builder.
    /// </summary>
    /// <param name="componentBuilder">
    ///     The component builder.
    /// </param>
    /// <param name="harness">
    ///     The harness.
    /// </param>
    /// <returns>
    ///     The component builder.
    /// </returns>
    public static IComponentBuilder AddHarness(this IComponentBuilder componentBuilder, IHarness harness) =>
        componentBuilder.Configure(harness.OnConfigure)
                        .AddDependency(harness);
}
