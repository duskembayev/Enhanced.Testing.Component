namespace Enhanced.Testing.Component;

/// <summary>
///     Represents a test harness.
/// </summary>
public interface IHarness : IComponentDependency
{
    /// <summary>
    ///     Configures the web host builder.
    /// </summary>
    /// <param name="builder">
    ///     The web host builder.
    /// </param>
    void OnConfigure(IWebHostBuilder builder);
}
