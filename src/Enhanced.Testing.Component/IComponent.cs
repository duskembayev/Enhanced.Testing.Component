namespace Enhanced.Testing.Component;

/// <summary>
///     Represents a component.
/// </summary>
public interface IComponent
{
    /// <summary>
    ///     The service provider.
    /// </summary>
    IServiceProvider Services { get; }

    /// <summary>
    ///     The default client options for creating a http client.
    /// </summary>
    WebApplicationFactoryClientOptions ClientOptions { get; }

    /// <summary>
    ///     Creates a new <see cref="HttpClient" /> instance with the default options.
    /// </summary>
    /// <returns>
    ///     The <see cref="HttpClient" /> instance.
    /// </returns>
    HttpClient CreateClient();

    /// <summary>
    ///     Creates a new <see cref="HttpClient" /> instance with the specified options.
    /// </summary>
    /// <param name="options">
    ///     The options to use.
    /// </param>
    /// <returns>
    ///     The <see cref="HttpClient" /> instance.
    /// </returns>
    HttpClient CreateClient(WebApplicationFactoryClientOptions options);

    /// <summary>
    ///     Creates a new instance of an <see cref="HttpClient" /> that can be used to send HttpRequestMessage to the server.
    ///     The base address
    ///     of the HttpClient instance will be set to http://localhost.
    /// </summary>
    /// <param name="handlers">
    ///     A list of DelegatingHandler instances to set up on the HttpClient.
    /// </param>
    /// <returns>
    ///     The <see cref="HttpClient" /> instance.
    /// </returns>
    HttpClient CreateDefaultClient(params DelegatingHandler[] handlers);

    /// <summary>
    ///     Creates a new instance of an <see cref="HttpClient" /> that can be used to send HttpRequestMessage to the server.
    /// </summary>
    /// <param name="baseAddress">
    ///     The base address of the HttpClient instance.
    /// </param>
    /// <param name="handlers">
    ///     A list of DelegatingHandler instances to set up on the HttpClient.
    /// </param>
    /// <returns>
    ///     The <see cref="HttpClient" /> instance.
    /// </returns>
    HttpClient CreateDefaultClient(Uri baseAddress, params DelegatingHandler[] handlers);

    /// <summary>
    ///     Start the component and its dependencies.
    /// </summary>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Stop the component and its dependencies.
    /// </summary>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    /// <returns></returns>
    Task StopAsync(CancellationToken cancellationToken);
}
