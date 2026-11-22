using System.Diagnostics;
using System.Reflection;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Enhanced.Testing.Component.WireMock;

/// <summary>
///     The stubs and the received requests of one gRPC service of a <see cref="WireMockGrpcHarness" />, for the
///     whole harness or for a <see cref="WireMockGrpcScope" />.
/// </summary>
/// <remarks>
///     <para>
///         A call is named by calling the generated client:
///         <c>client => client.SayHelloAsync(null)</c> stands for every call of <c>SayHello</c>, and
///         <c>client => client.SayHelloAsync(new HelloRequest { Name = "world" })</c> for the calls whose request
///         equals this message. The client is not connected to anything: the call only names the method.
///     </para>
///     <para>
///         When several stubs fit a call, a stub with a condition (a request or a predicate) answers before a
///         stub without one, and among equal stubs the one made last answers. So a fixture can set a default
///         answer and a test can add its own for the request it sends. Dispose the
///         <see cref="WireMockGrpcStub" /> to remove a stub.
///     </para>
/// </remarks>
/// <typeparam name="TClient">
///     The client of the service that <c>Grpc.Tools</c> generated, for example <c>Greeter.GreeterClient</c>.
/// </typeparam>
public sealed class WireMockGrpcService<TClient>
    where TClient : ClientBase<TClient>
{
    private const int ConditionalPriority = 1_000_000_000;
    private const int UnconditionalPriority = 2_000_000_000;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    private readonly WireMockGrpcHarness _harness;
    private readonly WireMockGrpcScope? _scope;
    private readonly GrpcCallRecorder _recorder = new();
    private readonly TClient _client;

    internal WireMockGrpcService(WireMockGrpcHarness harness, WireMockGrpcScope? scope)
    {
        _harness = harness;
        _scope = scope;
        Descriptor = FindDescriptor();
        _client = CreateClient(_recorder);
    }

    /// <summary>
    ///     The descriptor of the service.
    /// </summary>
    public ServiceDescriptor Descriptor { get; }

    /// <summary>
    ///     Answers the call with the message.
    /// </summary>
    /// <param name="call">
    ///     The call, named through the client: <c>client => client.SayHelloAsync(null)</c> for every call of the
    ///     method, or with a request message for the calls whose request equals it.
    /// </param>
    /// <param name="response">
    ///     The response message.
    /// </param>
    /// <typeparam name="TResponse">
    ///     The response message type of the method.
    /// </typeparam>
    /// <returns>
    ///     The stub; dispose it to remove the stub.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if <paramref name="call" /> does not make exactly one call of this service.
    /// </exception>
    /// <exception cref="NotSupportedException">
    ///     Thrown if <paramref name="call" /> names a streaming method or the blocking variant of a method.
    /// </exception>
    public WireMockGrpcStub Respond<TResponse>(Func<TClient, AsyncUnaryCall<TResponse>> call, TResponse response)
        where TResponse : class, IMessage
    {
        ArgumentNullException.ThrowIfNull(response);

        return Stub(call, Response.Create().WithGrpcMessage(response));
    }

    /// <summary>
    ///     Answers the calls whose request satisfies the predicate with the message.
    /// </summary>
    /// <param name="call">
    ///     The call, named through the client: <c>client => client.SayHelloAsync(null)</c>.
    /// </param>
    /// <param name="predicate">
    ///     The condition the request message must satisfy.
    /// </param>
    /// <param name="response">
    ///     The response message.
    /// </param>
    /// <typeparam name="TRequest">
    ///     The request message type of the method.
    /// </typeparam>
    /// <typeparam name="TResponse">
    ///     The response message type of the method.
    /// </typeparam>
    /// <returns>
    ///     The stub; dispose it to remove the stub.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if <paramref name="call" /> does not make exactly one call of this service, or the method does
    ///     not take <typeparamref name="TRequest" /> messages.
    /// </exception>
    /// <exception cref="NotSupportedException">
    ///     Thrown if <paramref name="call" /> names a streaming method or the blocking variant of a method.
    /// </exception>
    public WireMockGrpcStub Respond<TRequest, TResponse>(Func<TClient, AsyncUnaryCall<TResponse>> call,
        Func<TRequest, bool> predicate, TResponse response)
        where TRequest : IMessage<TRequest>, new()
        where TResponse : class, IMessage
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(response);

        return Stub(call, predicate, Response.Create().WithGrpcMessage(response));
    }

    /// <summary>
    ///     Fails the call with a gRPC status, as a service that fails the call does.
    /// </summary>
    /// <param name="call">
    ///     The call, named through the client: <c>client => client.SayHelloAsync(null)</c> for every call of the
    ///     method, or with a request message for the calls whose request equals it.
    /// </param>
    /// <param name="statusCode">
    ///     The status code.
    /// </param>
    /// <param name="message">
    ///     The status message; the client reads it from <see cref="Status.Detail" />.
    /// </param>
    /// <param name="details">
    ///     The error details of a rich status; see
    ///     <see cref="WireMockGrpcExtensions.WithGrpcStatus" />.
    /// </param>
    /// <typeparam name="TResponse">
    ///     The response message type of the method.
    /// </typeparam>
    /// <returns>
    ///     The stub; dispose it to remove the stub.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if <paramref name="call" /> does not make exactly one call of this service.
    /// </exception>
    /// <exception cref="NotSupportedException">
    ///     Thrown if <paramref name="call" /> names a streaming method or the blocking variant of a method.
    /// </exception>
    public WireMockGrpcStub Fail<TResponse>(Func<TClient, AsyncUnaryCall<TResponse>> call, StatusCode statusCode,
        string? message = null, params IMessage[] details)
        where TResponse : class, IMessage
    {
        return Stub(call, Response.Create().WithGrpcStatus(statusCode, message, details));
    }

    /// <summary>
    ///     Fails the calls whose request satisfies the predicate with a gRPC status.
    /// </summary>
    /// <param name="call">
    ///     The call, named through the client: <c>client => client.SayHelloAsync(null)</c>.
    /// </param>
    /// <param name="predicate">
    ///     The condition the request message must satisfy.
    /// </param>
    /// <param name="statusCode">
    ///     The status code.
    /// </param>
    /// <param name="message">
    ///     The status message; the client reads it from <see cref="Status.Detail" />.
    /// </param>
    /// <param name="details">
    ///     The error details of a rich status; see
    ///     <see cref="WireMockGrpcExtensions.WithGrpcStatus" />.
    /// </param>
    /// <typeparam name="TRequest">
    ///     The request message type of the method.
    /// </typeparam>
    /// <typeparam name="TResponse">
    ///     The response message type of the method.
    /// </typeparam>
    /// <returns>
    ///     The stub; dispose it to remove the stub.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if <paramref name="call" /> does not make exactly one call of this service, or the method does
    ///     not take <typeparamref name="TRequest" /> messages.
    /// </exception>
    /// <exception cref="NotSupportedException">
    ///     Thrown if <paramref name="call" /> names a streaming method or the blocking variant of a method.
    /// </exception>
    public WireMockGrpcStub Fail<TRequest, TResponse>(Func<TClient, AsyncUnaryCall<TResponse>> call,
        Func<TRequest, bool> predicate, StatusCode statusCode, string? message = null, params IMessage[] details)
        where TRequest : IMessage<TRequest>, new()
        where TResponse : class, IMessage
    {
        ArgumentNullException.ThrowIfNull(predicate);

        return Stub(call, predicate, Response.Create().WithGrpcStatus(statusCode, message, details));
    }

    /// <summary>
    ///     Returns the request messages of this type that the application sent to the service, in the order the
    ///     server received them. Calls that no stub answered are included.
    /// </summary>
    /// <remarks>
    ///     If several methods of the service take <typeparamref name="TRequest" />, the requests of all of them
    ///     are returned; name the call to get the requests of one method. In a scope, only the requests received
    ///     since the scope began are returned.
    /// </remarks>
    /// <typeparam name="TRequest">
    ///     The request message type.
    /// </typeparam>
    /// <returns>
    ///     The request messages.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if no unary method of the service takes <typeparamref name="TRequest" /> messages.
    /// </exception>
    public IReadOnlyList<TRequest> Requests<TRequest>()
        where TRequest : IMessage<TRequest>, new()
    {
        return Requests<TRequest>(_ => true);
    }

    /// <summary>
    ///     Returns the request messages of this type that the application sent to the service and that satisfy
    ///     the predicate, in the order the server received them.
    /// </summary>
    /// <remarks>
    ///     If several methods of the service take <typeparamref name="TRequest" />, the requests of all of them
    ///     are returned; name the call to get the requests of one method. In a scope, only the requests received
    ///     since the scope began are returned.
    /// </remarks>
    /// <param name="predicate">
    ///     The condition a request message must satisfy.
    /// </param>
    /// <typeparam name="TRequest">
    ///     The request message type.
    /// </typeparam>
    /// <returns>
    ///     The request messages.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if no unary method of the service takes <typeparamref name="TRequest" /> messages.
    /// </exception>
    public IReadOnlyList<TRequest> Requests<TRequest>(Func<TRequest, bool> predicate)
        where TRequest : IMessage<TRequest>, new()
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var methods = Descriptor.Methods
                                .Where(method => !method.IsClientStreaming && method.InputType.ClrType == typeof(TRequest))
                                .ToList();

        if (methods.Count == 0)
        {
            throw new ArgumentException(
                $"The gRPC service '{Descriptor.FullName}' has no unary method that takes {typeof(TRequest).Name} " +
                $"messages. Its methods: {DescribeMethods()}.");
        }

        return Sent<TRequest>(methods).Where(predicate).ToList();
    }

    /// <summary>
    ///     Returns the request messages of the call that the application sent, in the order the server received
    ///     them. Calls that no stub answered are included.
    /// </summary>
    /// <remarks>
    ///     The request type cannot be inferred from the call: write
    ///     <c>Requests&lt;HelloRequest, HelloReply&gt;(client => client.SayHelloAsync(null))</c>, or pass a
    ///     predicate. In a scope, only the requests received since the scope began are returned.
    /// </remarks>
    /// <param name="call">
    ///     The call, named through the client: <c>client => client.SayHelloAsync(null)</c> for every call of the
    ///     method, or with a request message for the calls whose request equals it.
    /// </param>
    /// <typeparam name="TRequest">
    ///     The request message type of the method.
    /// </typeparam>
    /// <typeparam name="TResponse">
    ///     The response message type of the method.
    /// </typeparam>
    /// <returns>
    ///     The request messages.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if <paramref name="call" /> does not make exactly one call of this service, or the method does
    ///     not take <typeparamref name="TRequest" /> messages.
    /// </exception>
    /// <exception cref="NotSupportedException">
    ///     Thrown if <paramref name="call" /> names a streaming method or the blocking variant of a method.
    /// </exception>
    public IReadOnlyList<TRequest> Requests<TRequest, TResponse>(Func<TClient, AsyncUnaryCall<TResponse>> call)
        where TRequest : IMessage<TRequest>, new()
        where TResponse : class, IMessage
    {
        return Requests<TRequest, TResponse>(call, _ => true);
    }

    /// <summary>
    ///     Returns the request messages of the call that the application sent and that satisfy the predicate, in
    ///     the order the server received them.
    /// </summary>
    /// <remarks>
    ///     In a scope, only the requests received since the scope began are returned.
    /// </remarks>
    /// <param name="call">
    ///     The call, named through the client: <c>client => client.SayHelloAsync(null)</c>.
    /// </param>
    /// <param name="predicate">
    ///     The condition a request message must satisfy.
    /// </param>
    /// <typeparam name="TRequest">
    ///     The request message type of the method.
    /// </typeparam>
    /// <typeparam name="TResponse">
    ///     The response message type of the method.
    /// </typeparam>
    /// <returns>
    ///     The request messages.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if <paramref name="call" /> does not make exactly one call of this service, or the method does
    ///     not take <typeparamref name="TRequest" /> messages.
    /// </exception>
    /// <exception cref="NotSupportedException">
    ///     Thrown if <paramref name="call" /> names a streaming method or the blocking variant of a method.
    /// </exception>
    public IReadOnlyList<TRequest> Requests<TRequest, TResponse>(Func<TClient, AsyncUnaryCall<TResponse>> call,
        Func<TRequest, bool> predicate)
        where TRequest : IMessage<TRequest>, new()
        where TResponse : class, IMessage
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var (method, expected) = Record<TRequest, TResponse>(call);

        return Sent<TRequest>([method]).Where(request => Fits(request, expected, predicate)).ToList();
    }

    /// <summary>
    ///     Waits until the application has sent a request of the call, and returns it. A request sent before the
    ///     wait began counts too.
    /// </summary>
    /// <remarks>
    ///     The request type cannot be inferred from the call: write
    ///     <c>WaitForRequestAsync&lt;HelloRequest, HelloReply&gt;(client => client.SayHelloAsync(null), timeout)</c>,
    ///     or pass a predicate. In a scope, only the requests received since the scope began count.
    /// </remarks>
    /// <param name="call">
    ///     The call, named through the client: <c>client => client.SayHelloAsync(null)</c> for every call of the
    ///     method, or with a request message for the calls whose request equals it.
    /// </param>
    /// <param name="timeout">
    ///     How long to wait for the request.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    /// <typeparam name="TRequest">
    ///     The request message type of the method.
    /// </typeparam>
    /// <typeparam name="TResponse">
    ///     The response message type of the method.
    /// </typeparam>
    /// <returns>
    ///     The first request of the call.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if <paramref name="call" /> does not make exactly one call of this service, or the method does
    ///     not take <typeparamref name="TRequest" /> messages.
    /// </exception>
    /// <exception cref="NotSupportedException">
    ///     Thrown if <paramref name="call" /> names a streaming method or the blocking variant of a method.
    /// </exception>
    /// <exception cref="TimeoutException">
    ///     Thrown if no such request arrived in time. The message tells how many requests of the method arrived.
    /// </exception>
    public Task<TRequest> WaitForRequestAsync<TRequest, TResponse>(Func<TClient, AsyncUnaryCall<TResponse>> call,
        TimeSpan timeout, CancellationToken cancellationToken = default)
        where TRequest : IMessage<TRequest>, new()
        where TResponse : class, IMessage
    {
        return WaitForRequestAsync<TRequest, TResponse>(call, _ => true, timeout, cancellationToken);
    }

    /// <summary>
    ///     Waits until the application has sent a request of the call that satisfies the predicate, and returns
    ///     it. A request sent before the wait began counts too.
    /// </summary>
    /// <remarks>
    ///     In a scope, only the requests received since the scope began count.
    /// </remarks>
    /// <param name="call">
    ///     The call, named through the client: <c>client => client.SayHelloAsync(null)</c>.
    /// </param>
    /// <param name="predicate">
    ///     The condition the request message must satisfy.
    /// </param>
    /// <param name="timeout">
    ///     How long to wait for the request.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    /// <typeparam name="TRequest">
    ///     The request message type of the method.
    /// </typeparam>
    /// <typeparam name="TResponse">
    ///     The response message type of the method.
    /// </typeparam>
    /// <returns>
    ///     The first request of the call that satisfies the predicate.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if <paramref name="call" /> does not make exactly one call of this service, or the method does
    ///     not take <typeparamref name="TRequest" /> messages.
    /// </exception>
    /// <exception cref="NotSupportedException">
    ///     Thrown if <paramref name="call" /> names a streaming method or the blocking variant of a method.
    /// </exception>
    /// <exception cref="TimeoutException">
    ///     Thrown if no such request arrived in time. The message tells how many requests of the method arrived.
    /// </exception>
    public async Task<TRequest> WaitForRequestAsync<TRequest, TResponse>(
        Func<TClient, AsyncUnaryCall<TResponse>> call, Func<TRequest, bool> predicate, TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TRequest : IMessage<TRequest>, new()
        where TResponse : class, IMessage
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var (method, expected) = Record<TRequest, TResponse>(call);
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sent = Sent<TRequest>([method]);

            foreach (var request in sent)
            {
                if (Fits(request, expected, predicate))
                {
                    return request;
                }
            }

            if (stopwatch.Elapsed >= timeout)
            {
                throw new TimeoutException(
                    $"No request of the gRPC call '{Descriptor.FullName}/{method.Name}' that satisfies the " +
                    $"condition arrived within {timeout.TotalMilliseconds:0} ms. {sent.Count} request(s) of this " +
                    $"method arrived{(_scope is null ? "" : " since the scope began")}" +
                    (sent.Count == 0 ? "." : $"; the last one: {JsonFormatter.Default.Format(sent[^1])}"));
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private WireMockGrpcStub Stub<TResponse>(Func<TClient, AsyncUnaryCall<TResponse>> call,
        IResponseBuilder response)
    {
        var (method, expected) = Record(call);
        var request = Request.Create().UsingGrpc(Descriptor, method.Name);

        if (expected is not null)
        {
            request = request.WithGrpcMessage(method.InputType, expected.Equals);
        }

        return Stub(request, expected is not null, response);
    }

    private WireMockGrpcStub Stub<TRequest, TResponse>(Func<TClient, AsyncUnaryCall<TResponse>> call,
        Func<TRequest, bool> predicate, IResponseBuilder response)
        where TRequest : IMessage<TRequest>, new()
    {
        var (method, expected) = Record(call);
        var request = Request.Create()
                             .UsingGrpc(Descriptor, method.Name)
                             .WithGrpcMessage<TRequest>(
                                 message => (expected is null || expected.Equals(message)) && predicate(message));

        return Stub(request, true, response);
    }

    private WireMockGrpcStub Stub(IRequestBuilder request, bool conditional, IResponseBuilder response)
    {
        var server = _harness.Server;
        var id = Guid.NewGuid();
        var priority = (conditional ? ConditionalPriority : UnconditionalPriority) - _harness.NextStub();

        var stub = new WireMockGrpcStub(server, id);

        _scope?.Add(stub);
        server.Given(request).WithGuid(id).AtPriority(priority).RespondWith(response);

        return stub;
    }

    private List<TRequest> Sent<TRequest>(IReadOnlyCollection<MethodDescriptor> methods)
        where TRequest : IMessage<TRequest>, new()
    {
        var paths = methods.Select(method => $"/{Descriptor.FullName}/{method.Name}").ToHashSet(StringComparer.Ordinal);
        var since = _scope?.Began ?? DateTime.MinValue;
        var requests = new List<TRequest>();

        var sent = _harness.Server.LogEntries
                           .Select(entry => entry.RequestMessage)
                           .Where(message => message is not null && message.DateTime >= since && paths.Contains(message.Path))
                           .OrderBy(message => message!.DateTime);

        foreach (var message in sent)
        {
            if (WireMockGrpcExtensions.TryReadMessage<TRequest>(message!.BodyAsBytes, out var request))
            {
                requests.Add(request);
            }
        }

        return requests;
    }

    private static bool Fits<TRequest>(TRequest request, IMessage? expected, Func<TRequest, bool> predicate) =>
        (expected is null || expected.Equals(request)) && predicate(request);

    private (MethodDescriptor Method, IMessage? Request) Record<TRequest, TResponse>(
        Func<TClient, AsyncUnaryCall<TResponse>> call)
    {
        var recorded = Record(call);

        if (recorded.Method.InputType.ClrType != typeof(TRequest))
        {
            throw new ArgumentException(
                $"The gRPC method '{Descriptor.FullName}/{recorded.Method.Name}' takes " +
                $"'{recorded.Method.InputType.FullName}' messages ({recorded.Method.InputType.ClrType.Name}), not " +
                $"{typeof(TRequest).Name}.");
        }

        return recorded;
    }

    private (MethodDescriptor Method, IMessage? Request) Record<TResponse>(
        Func<TClient, AsyncUnaryCall<TResponse>> call)
    {
        ArgumentNullException.ThrowIfNull(call);

        var calls = _recorder.Record(() => call(_client));

        if (calls.Count != 1)
        {
            throw new ArgumentException(
                $"A stub is named by one call of {typeof(TClient).Name}, for example " +
                $"'client => client.{Descriptor.Methods.FirstOrDefault()?.Name ?? "Method"}Async(null)'; " +
                $"this one made {calls.Count} calls.", nameof(call));
        }

        var method = calls[0].Service == Descriptor.FullName ? Descriptor.FindMethodByName(calls[0].Method) : null;

        if (method is null)
        {
            throw new ArgumentException(
                $"The gRPC call '{calls[0].Service}/{calls[0].Method}' is not a method of the service " +
                $"'{Descriptor.FullName}'. Its methods: {DescribeMethods()}.", nameof(call));
        }

        return (method, calls[0].Request is IMessage request ? method.InputType.Parser.ParseFrom(request.ToByteArray()) : null);
    }

    private string DescribeMethods() =>
        string.Join(", ", Descriptor.Methods.Select(method => $"{method.Name}({method.InputType.Name})"));

    private static ServiceDescriptor FindDescriptor()
    {
        var descriptor = typeof(TClient).DeclaringType?
                                        .GetProperty(nameof(Descriptor), BindingFlags.Public | BindingFlags.Static)?
                                        .GetValue(null) as ServiceDescriptor;

        return descriptor ?? throw new InvalidOperationException(
            $"'{typeof(TClient).FullName}' is not a gRPC client generated by Grpc.Tools: the type that declares it " +
            "has no static 'Descriptor' property with the service descriptor.");
    }

    private static TClient CreateClient(CallInvoker invoker)
    {
        try
        {
            return (TClient)Activator.CreateInstance(typeof(TClient), invoker)!;
        }
        catch (MissingMethodException exception)
        {
            throw new InvalidOperationException(
                $"'{typeof(TClient).FullName}' is not a gRPC client generated by Grpc.Tools: it has no public " +
                $"constructor that takes a {nameof(CallInvoker)}.", exception);
        }
    }
}
