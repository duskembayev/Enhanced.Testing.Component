namespace Enhanced.Testing.Component;

/// <summary>
///     The harness for creating an <see cref="HttpClient" />.
/// </summary>
public class HttpClientHarness : Harness
{
    /// <summary>
    ///     Creates an <see cref="HttpClient" /> that calls the application under test.
    /// </summary>
    /// <returns>
    ///     The HTTP client.
    /// </returns>
    public HttpClient CreateClient() => Component.CreateClient();
}
