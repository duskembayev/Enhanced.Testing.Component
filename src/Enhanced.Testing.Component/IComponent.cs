using Microsoft.AspNetCore.TestHost;

namespace Enhanced.Testing.Component;

/// <summary>
///     Represents a component.
/// </summary>
public interface IComponent
{
    /// <summary>
    ///     The test server.
    /// </summary>
    public TestServer Server { get; }

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
    ///     Start the dependencies in order, then the application.
    ///     If starting fails, everything started so far is stopped before the exception is rethrown.
    ///     A component can be started only once.
    /// </summary>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Shut down the application while its dependencies are still running, then stop the dependencies in reverse
    ///     order. A failing dependency does not prevent the others from stopping; the failures are rethrown afterwards
    ///     and calling this method again retries only the steps that did not complete.
    /// </summary>
    /// <param name="cancellationToken">
    ///     The cancellation token. Cancellation is observed between steps: a step in progress is completed, the
    ///     remaining steps are skipped and <see cref="OperationCanceledException" /> is thrown.
    /// </param>
    Task StopAsync(CancellationToken cancellationToken);
}
