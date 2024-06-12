namespace Enhanced.Testing.Component;

/// <summary>
///     The harness for creating an <see cref="HttpClient" />.
/// </summary>
public class HttpClientHarness : Harness
{
    /// <summary>
    ///     Creates a new instance of the <see cref="HttpClientHarness" /> class.
    /// </summary>
    /// <returns>
    ///     The <see cref="HttpClientHarness" /> instance.
    /// </returns>
    public HttpClient CreateClient() => Component.CreateClient();
}
