using System.Diagnostics;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Enhanced.Testing.Component.WireMock;

/// <summary>
///     The calls of a gRPC method selected with <c>On</c> of a <see cref="WireMockGrpcHarness" />: stub them with
///     <see cref="Respond" /> and <see cref="Fail" />, or read what the application sent with
///     <see cref="Requests" /> and <see cref="WaitForRequestAsync" />.
/// </summary>
/// <remarks>
///     The predicate given to <c>On</c> should only read the request: it runs for every call of the method and
///     may run more than once for the same request. A predicate that throws does not select the request.
/// </remarks>
/// <typeparam name="TRequest">
///     The request message type of the method.
/// </typeparam>
public sealed class WireMockGrpcCall<TRequest>
    where TRequest : IMessage<TRequest>, new()
{
    private const int ConditionalPriority = 1_000_000_000;
    private const int UnconditionalPriority = 2_000_000_000;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    private readonly WireMockGrpcHarness _harness;
    private readonly IReadOnlyList<MethodDescriptor> _methods;
    private readonly Func<TRequest, bool>? _predicate;

    internal WireMockGrpcCall(WireMockGrpcHarness harness, IReadOnlyList<MethodDescriptor> methods,
        Func<TRequest, bool>? predicate)
    {
        _harness = harness;
        _methods = methods;
        _predicate = predicate;
    }

    /// <summary>
    ///     Answers the calls with the message.
    /// </summary>
    /// <param name="response">
    ///     The response message. If several methods take <typeparamref name="TRequest" />, its type selects the
    ///     method.
    /// </param>
    /// <returns>
    ///     The stub. It stays until the harness is reset; dispose it to remove it earlier.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if no selected method returns messages of this type, or several do.
    /// </exception>
    public WireMockGrpcStub Respond(IMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var returning = _methods.Where(method => method.OutputType.FullName == response.Descriptor.FullName).ToList();

        if (returning.Count == 0)
        {
            throw new ArgumentException(
                $"No gRPC method that takes {typeof(TRequest).Name} messages returns {response.Descriptor.Name} " +
                $"messages. The methods: {Describe(_methods)}.", nameof(response));
        }

        return Stub(Single(returning), Response.Create().WithGrpcMessage(response));
    }

    /// <summary>
    ///     Fails the calls with a gRPC status, as a service that fails the call does.
    /// </summary>
    /// <param name="statusCode">
    ///     The status code, other than <see cref="StatusCode.OK" />.
    /// </param>
    /// <param name="message">
    ///     The status message; the client reads it from <see cref="Status.Detail" />.
    /// </param>
    /// <param name="details">
    ///     The error details of a rich status; see <see cref="WireMockGrpcExtensions.WithGrpcStatus" />.
    /// </param>
    /// <returns>
    ///     The stub. It stays until the harness is reset; dispose it to remove it earlier.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if several methods are selected, or the status code is <see cref="StatusCode.OK" />.
    /// </exception>
    public WireMockGrpcStub Fail(StatusCode statusCode, string? message = null, params IMessage[] details)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (statusCode == StatusCode.OK)
        {
            throw new ArgumentException(
                $"A call cannot fail with the {nameof(StatusCode.OK)} status: answer it with {nameof(Respond)}.",
                nameof(statusCode));
        }

        return Stub(Single(_methods), Response.Create().WithGrpcStatus(statusCode, message, details));
    }

    /// <summary>
    ///     Returns the request messages of the calls that the application sent, in the order the server received
    ///     them. Calls that no stub answered are included.
    /// </summary>
    /// <remarks>
    ///     The server logs a request when it has produced the response, so the request of a call that a stub
    ///     answers with a delay appears after the delay.
    /// </remarks>
    /// <returns>
    ///     The request messages.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if several methods are selected.
    /// </exception>
    public IReadOnlyList<TRequest> Requests()
    {
        return Read(Single(_methods), new RequestLog());
    }

    /// <summary>
    ///     Waits until the application has sent a request of the calls, and returns it. A request that is already
    ///     in the request log counts too.
    /// </summary>
    /// <remarks>
    ///     The server logs a request when it has produced the response, so the request of a call that a stub
    ///     answers with a delay appears after the delay.
    /// </remarks>
    /// <param name="timeout">
    ///     How long to wait for the request; <see cref="Timeout.InfiniteTimeSpan" /> waits until the request
    ///     arrives or the wait is cancelled.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    /// <returns>
    ///     The first request of the calls in the request log.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if several methods are selected.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown if <paramref name="timeout" /> is negative and not <see cref="Timeout.InfiniteTimeSpan" />.
    /// </exception>
    /// <exception cref="TimeoutException">
    ///     Thrown if no such request arrived in time. The message tells how many requests of the method the
    ///     request log has.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     Thrown if the wait is cancelled.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the component stops during the wait.
    /// </exception>
    public async Task<TRequest> WaitForRequestAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout,
                $"The timeout must not be negative; pass {nameof(Timeout)}.{nameof(Timeout.InfiniteTimeSpan)} to wait " +
                "without a limit.");
        }

        var method = Single(_methods);
        var log = new RequestLog();
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var selected = Read(method, log);

            if (selected.Count > 0)
            {
                return selected[0];
            }

            if (timeout != Timeout.InfiniteTimeSpan && stopwatch.Elapsed >= timeout)
            {
                throw new TimeoutException(DescribeTimeout(method, timeout, log));
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static string Describe(IEnumerable<MethodDescriptor> methods) =>
        string.Join(", ", methods.Select(
            method => $"{method.Service.FullName}/{method.Name}({method.InputType.Name}) returns {method.OutputType.Name}"));

    private static MethodDescriptor Single(IReadOnlyList<MethodDescriptor> methods)
    {
        if (methods.Count == 1)
        {
            return methods[0];
        }

        var sameService = methods.Select(method => method.Service.FullName).Distinct().Count() == 1;
        var sameName = methods.Select(method => method.Name).Distinct().Count() == 1;
        var missing = sameService ? "the method" : sameName ? "the service" : "the method or the service";

        throw new ArgumentException(
            $"Several gRPC methods take {typeof(TRequest).Name} messages: {Describe(methods)}. Name {missing} in On.");
    }

    private string DescribeTimeout(MethodDescriptor method, TimeSpan timeout, RequestLog log)
    {
        var description =
            $"No request of the gRPC method '{method.Service.FullName}/{method.Name}'" +
            $"{(_predicate is null ? "" : " that satisfies the condition")} arrived within " +
            $"{timeout.TotalMilliseconds:0} ms. The request log has {log.Read} request(s) of this method";

        if (log.Unreadable > 0)
        {
            description += $" and {log.Unreadable} more that could not be read (compressed, or not one message)";
        }

        if (log.Last is not null)
        {
            description += $"; the last one: {GrpcUnmatchedCalls.Shorten(JsonFormatter.Default.Format(log.Last))}";
        }

        description += ".";

        if (log.Failure is not null)
        {
            description += $" The condition threw {log.Failure.GetType().Name} for {log.Failed} of them: " +
                           $"{GrpcUnmatchedCalls.Shorten(log.Failure.Message)}";
        }

        return description;
    }

    private List<TRequest> Read(MethodDescriptor method, RequestLog log)
    {
        var path = $"/{method.Service.FullName}/{method.Name}";
        var selected = new List<TRequest>();

        var entries = _harness.Server.LogEntries
                              .Where(entry => entry.RequestMessage is not null && entry.RequestMessage.Path == path)
                              .OrderBy(entry => entry.RequestMessage!.DateTime);

        foreach (var entry in entries)
        {
            if (!log.Examined.Add(entry.Guid))
            {
                continue;
            }

            if (!WireMockGrpcExtensions.TryReadMessage<TRequest>(entry.RequestMessage!.BodyAsBytes, out var request))
            {
                log.Unreadable++;
                continue;
            }

            log.Read++;
            log.Last = request;

            try
            {
                if (_predicate is null || _predicate(request))
                {
                    selected.Add(request);
                }
            }
            catch (Exception exception)
            {
                log.Failed++;
                log.Failure = exception;
            }
        }

        return selected;
    }

    private WireMockGrpcStub Stub(MethodDescriptor method, IResponseBuilder response)
    {
        var request = Request.Create().UsingGrpc(method.Service, method.Name);

        if (_predicate is not null)
        {
            request = request.WithGrpcMessage(_predicate);
        }

        var server = _harness.Server;
        var id = Guid.NewGuid();
        var priority = (_predicate is null ? UnconditionalPriority : ConditionalPriority) - _harness.NextStub();

        server.Given(request).WithGuid(id).AtPriority(priority).RespondWith(response);

        return new WireMockGrpcStub(server, id);
    }

    private sealed class RequestLog
    {
        public HashSet<Guid> Examined { get; } = [];

        public int Read { get; set; }

        public int Unreadable { get; set; }

        public int Failed { get; set; }

        public Exception? Failure { get; set; }

        public IMessage? Last { get; set; }
    }
}
