using WireMock.Server;

namespace Enhanced.Testing.Component.WireMock;

/// <summary>
///     A stub of a gRPC call made by <see cref="WireMockGrpcService{TClient}" />. Dispose it to remove the stub, so
///     that the stubs made before it answer again.
/// </summary>
public sealed class WireMockGrpcStub : IDisposable
{
    private readonly WireMockServer _server;

    internal WireMockGrpcStub(WireMockServer server, Guid id)
    {
        _server = server;
        Id = id;
    }

    /// <summary>
    ///     The identifier of the WireMock mapping behind the stub.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    ///     Removes the stub from the server. Removing a stub that is already gone does nothing.
    /// </summary>
    public void Dispose()
    {
        _server.DeleteMapping(Id);
    }
}
