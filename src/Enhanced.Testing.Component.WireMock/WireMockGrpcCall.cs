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
    /// <exception cref="ArgumentException">
    ///     Thrown if no selected method returns messages of this type, or several do.
    /// </exception>
    public void Respond(IMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var returning = _methods.Where(method => method.OutputType.FullName == response.Descriptor.FullName).ToList();

        if (returning.Count == 0)
        {
            throw new ArgumentException(
                $"No gRPC method that takes {typeof(TRequest).Name} messages returns {response.Descriptor.Name} " +
                $"messages. The methods: {Describe(_methods)}.", nameof(response));
        }

        Stub(Single(returning), Response.Create().WithGrpcMessage(response));
    }

    /// <summary>
    ///     Fails the calls with a gRPC status, as a service that fails the call does.
    /// </summary>
    /// <param name="statusCode">
    ///     The status code.
    /// </param>
    /// <param name="message">
    ///     The status message; the client reads it from <see cref="Status.Detail" />.
    /// </param>
    /// <param name="details">
    ///     The error details of a rich status; see <see cref="WireMockGrpcExtensions.WithGrpcStatus" />.
    /// </param>
    /// <exception cref="ArgumentException">
    ///     Thrown if several methods are selected.
    /// </exception>
    public void Fail(StatusCode statusCode, string? message = null, params IMessage[] details)
    {
        Stub(Single(_methods), Response.Create().WithGrpcStatus(statusCode, message, details));
    }

    /// <summary>
    ///     Returns the request messages of the calls that the application sent, in the order the server received
    ///     them. Calls that no stub answered are included.
    /// </summary>
    /// <returns>
    ///     The request messages.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if several methods are selected.
    /// </exception>
    public IReadOnlyList<TRequest> Requests()
    {
        return Sent(Single(_methods)).Where(Selected).ToList();
    }

    /// <summary>
    ///     Waits until the application has sent a request of the calls, and returns it. A request sent before the
    ///     wait began counts too.
    /// </summary>
    /// <param name="timeout">
    ///     How long to wait for the request.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    /// <returns>
    ///     The first request of the calls.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if several methods are selected.
    /// </exception>
    /// <exception cref="TimeoutException">
    ///     Thrown if no such request arrived in time. The message tells how many requests of the method arrived.
    /// </exception>
    public async Task<TRequest> WaitForRequestAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var method = Single(_methods);
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sent = Sent(method);

            foreach (var request in sent)
            {
                if (Selected(request))
                {
                    return request;
                }
            }

            if (stopwatch.Elapsed >= timeout)
            {
                throw new TimeoutException(
                    $"No request of the gRPC method '{method.Service.FullName}/{method.Name}'" +
                    $"{(_predicate is null ? "" : " that satisfies the condition")} arrived within " +
                    $"{timeout.TotalMilliseconds:0} ms. {sent.Count} request(s) of this method arrived" +
                    (sent.Count == 0 ? "." : $"; the last one: {JsonFormatter.Default.Format(sent[^1])}"));
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

        throw new ArgumentException(
            $"Several gRPC methods take {typeof(TRequest).Name} messages: {Describe(methods)}. Name the method or " +
            "the service in On.");
    }

    private bool Selected(TRequest request) => _predicate is null || _predicate(request);

    private List<TRequest> Sent(MethodDescriptor method)
    {
        var path = $"/{method.Service.FullName}/{method.Name}";
        var requests = new List<TRequest>();

        var sent = _harness.Server.LogEntries
                           .Select(entry => entry.RequestMessage)
                           .Where(message => message is not null && message.Path == path)
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

    private void Stub(MethodDescriptor method, IResponseBuilder response)
    {
        var request = Request.Create().UsingGrpc(method.Service, method.Name);

        if (_predicate is not null)
        {
            request = request.WithGrpcMessage(_predicate);
        }

        var priority = (_predicate is null ? UnconditionalPriority : ConditionalPriority) - _harness.NextStub();

        _harness.Server.Given(request).AtPriority(priority).RespondWith(response);
    }
}
