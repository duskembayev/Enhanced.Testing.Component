using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Enhanced.Testing.Component.WireMock;

/// <summary>
///     The calls of a gRPC method selected with <c>On</c> of a <see cref="WireMockGrpcHarness" />.
/// </summary>
/// <typeparam name="TRequest">
///     The request message type of the method.
/// </typeparam>
public sealed class WireMockGrpcCall<TRequest>
    where TRequest : IMessage<TRequest>, new()
{
    private const int ConditionalPriority = 1_000_000_000;
    private const int UnconditionalPriority = 2_000_000_000;

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
