using System.Buffers.Binary;
using System.Globalization;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Enhanced.Testing.Component.WireMock;

/// <summary>
///     Extensions that describe gRPC calls and responses with the generated message types. The server must listen
///     over HTTP/2; see <see cref="WireMockHarness.UseHttp2" />.
/// </summary>
public static class WireMockGrpcExtensions
{
    private const string ContentType = "application/grpc";
    private const string AsyncSuffix = "Async";
    private const int MessagePrefixLength = 5;

    /// <summary>
    ///     Matches calls of a gRPC method.
    /// </summary>
    /// <param name="request">
    ///     The request builder.
    /// </param>
    /// <param name="method">
    ///     The full method name: the service name with its package, a slash and the method name, for example
    ///     <c>greet.Greeter/SayHello</c>.
    /// </param>
    /// <returns>
    ///     The request builder.
    /// </returns>
    public static IRequestBuilder UsingGrpc(this IRequestBuilder request, string method)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(method);

        method = method.TrimStart('/');
        GrpcStub.For(request).Method = method;

        return request.UsingPost().WithPath("/" + method);
    }

    /// <summary>
    ///     Matches calls of a gRPC method of the service, named through the generated code:
    ///     <c>UsingGrpc(Greeter.Descriptor, nameof(Greeter.GreeterClient.SayHelloAsync))</c>.
    /// </summary>
    /// <param name="request">
    ///     The request builder.
    /// </param>
    /// <param name="service">
    ///     The service descriptor from the generated code, for example <c>Greeter.Descriptor</c>.
    /// </param>
    /// <param name="method">
    ///     The method name, as in the <c>.proto</c> file or as the client names it (with the <c>Async</c> suffix).
    /// </param>
    /// <returns>
    ///     The request builder.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if the service has no method with this name.
    /// </exception>
    public static IRequestBuilder UsingGrpc(this IRequestBuilder request, ServiceDescriptor service, string method)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrEmpty(method);

        var descriptor = service.FindMethodByName(method);

        if (descriptor is null && method.EndsWith(AsyncSuffix, StringComparison.Ordinal))
        {
            descriptor = service.FindMethodByName(method[..^AsyncSuffix.Length]);
        }

        if (descriptor is null)
        {
            throw new ArgumentException(
                $"The gRPC service '{service.FullName}' has no method '{method}'.", nameof(method));
        }

        request = request.UsingGrpc($"{service.FullName}/{descriptor.Name}");

        var stub = GrpcStub.For(request);
        var parser = descriptor.InputType.Parser;

        stub.RequestDescriptor = descriptor.InputType;
        stub.DescribeRequest ??= body => TryReadPayload(body, out var payload) ? Format(parser, payload) : null;
        stub.ThrowIfRequestTypeDoesNotFit();

        return request;
    }

    /// <summary>
    ///     Matches calls whose request message satisfies the predicate.
    /// </summary>
    /// <param name="request">
    ///     The request builder.
    /// </param>
    /// <param name="predicate">
    ///     The condition the request message must satisfy.
    /// </param>
    /// <typeparam name="TRequest">
    ///     The generated request message type.
    /// </typeparam>
    /// <returns>
    ///     The request builder.
    /// </returns>
    public static IRequestBuilder WithGrpcMessage<TRequest>(this IRequestBuilder request,
        Func<TRequest, bool> predicate)
        where TRequest : IMessage<TRequest>, new()
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(predicate);

        var stub = GrpcStub.For(request);

        stub.RequestType = typeof(TRequest);
        stub.DescribeRequest = body =>
            TryReadMessage<TRequest>(body, out var message) ? JsonFormatter.Default.Format(message) : null;
        stub.ThrowIfRequestTypeDoesNotFit();

        return request.WithBody(body => TryReadMessage<TRequest>(body, out var message) && predicate(message));
    }

    /// <summary>
    ///     Responds to a unary call with the message and the <see cref="StatusCode.OK" /> status.
    /// </summary>
    /// <param name="response">
    ///     The response builder.
    /// </param>
    /// <param name="message">
    ///     The response message.
    /// </param>
    /// <returns>
    ///     The response builder.
    /// </returns>
    public static IResponseBuilder WithGrpcMessage(this IResponseBuilder response, IMessage message)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(message);

        return response.WithHeader("Content-Type", ContentType)
                       .WithTrailingHeader("grpc-status", "0")
                       .WithBody(WriteMessage(message));
    }

    /// <summary>
    ///     Responds to a call with a gRPC status and no message, as a service that fails the call does.
    /// </summary>
    /// <param name="response">
    ///     The response builder.
    /// </param>
    /// <param name="statusCode">
    ///     The status code.
    /// </param>
    /// <param name="detail">
    ///     The status detail the client receives.
    /// </param>
    /// <returns>
    ///     The response builder.
    /// </returns>
    public static IResponseBuilder WithGrpcStatus(this IResponseBuilder response, StatusCode statusCode,
        string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(response);

        response = response.WithHeader("Content-Type", ContentType)
                           .WithHeader("grpc-status", ((int)statusCode).ToString(CultureInfo.InvariantCulture));

        return detail is null ? response : response.WithHeader("grpc-message", Uri.EscapeDataString(detail));
    }

    private static bool TryReadMessage<TMessage>(byte[]? body, out TMessage message)
        where TMessage : IMessage<TMessage>, new()
    {
        message = new TMessage();

        if (!TryReadPayload(body, out var payload))
        {
            return false;
        }

        try
        {
            message.MergeFrom(payload);
            return true;
        }
        catch (InvalidProtocolBufferException)
        {
            return false;
        }
    }

    private static bool TryReadPayload(byte[]? body, out byte[] payload)
    {
        payload = [];

        if (body is null || body.Length < MessagePrefixLength || body[0] != 0)
        {
            return false;
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(1, 4));

        if (length != body.Length - MessagePrefixLength)
        {
            return false;
        }

        payload = body[MessagePrefixLength..];
        return true;
    }

    private static string? Format(MessageParser parser, byte[] payload)
    {
        try
        {
            return JsonFormatter.Default.Format(parser.ParseFrom(payload));
        }
        catch (InvalidProtocolBufferException)
        {
            return null;
        }
    }

    private static byte[] WriteMessage(IMessage message)
    {
        var size = message.CalculateSize();
        var body = new byte[MessagePrefixLength + size];

        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(1, 4), (uint)size);
        message.WriteTo(body.AsSpan(MessagePrefixLength));

        return body;
    }
}
