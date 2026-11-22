using Google.Protobuf;
using Grpc.Core;

namespace Enhanced.Testing.Component.WireMock;

/// <summary>
///     A WireMock harness that fakes a gRPC service the application calls. The server listens over HTTP/2, and
///     stubs are described through the generated client of the service:
///     <c>greeter.Respond(client => client.SayHelloAsync(null), new HelloReply { Message = "Hello" })</c>.
/// </summary>
/// <remarks>
///     The members are those of <see cref="WireMockGrpcService{TClient}" />; see it for how a call is named and
///     which stub answers. Other services of the same backend are available through
///     <see cref="WireMockGrpcHarness.Service{TClient}" />.
/// </remarks>
/// <typeparam name="TClient">
///     The client of the service that <c>Grpc.Tools</c> generated, for example <c>Greeter.GreeterClient</c>.
/// </typeparam>
public class WireMockGrpcHarness<TClient> : WireMockGrpcHarness
    where TClient : ClientBase<TClient>
{
    /// <inheritdoc cref="WireMockGrpcService{TClient}.Respond{TResponse}(Func{TClient, AsyncUnaryCall{TResponse}}, TResponse)" />
    public WireMockGrpcStub Respond<TResponse>(Func<TClient, AsyncUnaryCall<TResponse>> call, TResponse response)
        where TResponse : class, IMessage
    {
        return Service<TClient>().Respond(call, response);
    }

    /// <inheritdoc cref="WireMockGrpcService{TClient}.Respond{TRequest, TResponse}(Func{TClient, AsyncUnaryCall{TResponse}}, Func{TRequest, bool}, TResponse)" />
    public WireMockGrpcStub Respond<TRequest, TResponse>(Func<TClient, AsyncUnaryCall<TResponse>> call,
        Func<TRequest, bool> predicate, TResponse response)
        where TRequest : IMessage<TRequest>, new()
        where TResponse : class, IMessage
    {
        return Service<TClient>().Respond(call, predicate, response);
    }

    /// <inheritdoc cref="WireMockGrpcService{TClient}.Fail{TResponse}(Func{TClient, AsyncUnaryCall{TResponse}}, StatusCode, string?, IMessage[])" />
    public WireMockGrpcStub Fail<TResponse>(Func<TClient, AsyncUnaryCall<TResponse>> call, StatusCode statusCode,
        string? message = null, params IMessage[] details)
        where TResponse : class, IMessage
    {
        return Service<TClient>().Fail(call, statusCode, message, details);
    }

    /// <inheritdoc cref="WireMockGrpcService{TClient}.Fail{TRequest, TResponse}(Func{TClient, AsyncUnaryCall{TResponse}}, Func{TRequest, bool}, StatusCode, string?, IMessage[])" />
    public WireMockGrpcStub Fail<TRequest, TResponse>(Func<TClient, AsyncUnaryCall<TResponse>> call,
        Func<TRequest, bool> predicate, StatusCode statusCode, string? message = null, params IMessage[] details)
        where TRequest : IMessage<TRequest>, new()
        where TResponse : class, IMessage
    {
        return Service<TClient>().Fail(call, predicate, statusCode, message, details);
    }

    /// <inheritdoc cref="WireMockGrpcService{TClient}.Requests{TRequest}()" />
    public IReadOnlyList<TRequest> Requests<TRequest>()
        where TRequest : IMessage<TRequest>, new()
    {
        return Service<TClient>().Requests<TRequest>();
    }

    /// <inheritdoc cref="WireMockGrpcService{TClient}.Requests{TRequest}(Func{TRequest, bool})" />
    public IReadOnlyList<TRequest> Requests<TRequest>(Func<TRequest, bool> predicate)
        where TRequest : IMessage<TRequest>, new()
    {
        return Service<TClient>().Requests(predicate);
    }
}
