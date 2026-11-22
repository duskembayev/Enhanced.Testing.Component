using Google.Protobuf;
using Grpc.Core;

namespace Enhanced.Testing.Component.WireMock;

/// <summary>
///     A scope of a <see cref="WireMockGrpcHarness{TClient}" />, for one test that shares the harness with others:
///     the stubs made through the scope are removed when it is disposed, and it sees only the requests received
///     since it began.
/// </summary>
/// <remarks>
///     The members are those of <see cref="WireMockGrpcService{TClient}" />.
/// </remarks>
/// <typeparam name="TClient">
///     The client of the service that <c>Grpc.Tools</c> generated, for example <c>Greeter.GreeterClient</c>.
/// </typeparam>
public sealed class WireMockGrpcScope<TClient> : WireMockGrpcScope
    where TClient : ClientBase<TClient>
{
    internal WireMockGrpcScope(WireMockGrpcHarness harness)
        : base(harness)
    {
    }

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

    /// <inheritdoc cref="WireMockGrpcService{TClient}.Requests{TRequest, TResponse}(Func{TClient, AsyncUnaryCall{TResponse}})" />
    public IReadOnlyList<TRequest> Requests<TRequest, TResponse>(Func<TClient, AsyncUnaryCall<TResponse>> call)
        where TRequest : IMessage<TRequest>, new()
        where TResponse : class, IMessage
    {
        return Service<TClient>().Requests<TRequest, TResponse>(call);
    }

    /// <inheritdoc cref="WireMockGrpcService{TClient}.Requests{TRequest, TResponse}(Func{TClient, AsyncUnaryCall{TResponse}}, Func{TRequest, bool})" />
    public IReadOnlyList<TRequest> Requests<TRequest, TResponse>(Func<TClient, AsyncUnaryCall<TResponse>> call,
        Func<TRequest, bool> predicate)
        where TRequest : IMessage<TRequest>, new()
        where TResponse : class, IMessage
    {
        return Service<TClient>().Requests(call, predicate);
    }

    /// <inheritdoc cref="WireMockGrpcService{TClient}.WaitForRequestAsync{TRequest, TResponse}(Func{TClient, AsyncUnaryCall{TResponse}}, TimeSpan, CancellationToken)" />
    public Task<TRequest> WaitForRequestAsync<TRequest, TResponse>(Func<TClient, AsyncUnaryCall<TResponse>> call,
        TimeSpan timeout, CancellationToken cancellationToken = default)
        where TRequest : IMessage<TRequest>, new()
        where TResponse : class, IMessage
    {
        return Service<TClient>().WaitForRequestAsync<TRequest, TResponse>(call, timeout, cancellationToken);
    }

    /// <inheritdoc cref="WireMockGrpcService{TClient}.WaitForRequestAsync{TRequest, TResponse}(Func{TClient, AsyncUnaryCall{TResponse}}, Func{TRequest, bool}, TimeSpan, CancellationToken)" />
    public Task<TRequest> WaitForRequestAsync<TRequest, TResponse>(Func<TClient, AsyncUnaryCall<TResponse>> call,
        Func<TRequest, bool> predicate, TimeSpan timeout, CancellationToken cancellationToken = default)
        where TRequest : IMessage<TRequest>, new()
        where TResponse : class, IMessage
    {
        return Service<TClient>().WaitForRequestAsync(call, predicate, timeout, cancellationToken);
    }
}
