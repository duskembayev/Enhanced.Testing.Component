using System.Buffers.Binary;
using System.Globalization;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Types;

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
    private const string BinarySuffix = "-bin";
    private const string ReservedPrefix = "grpc-";
    private const string DetailsTrailer = "grpc-status-details-bin";
    private const string MessageTrailer = "grpc-message";
    private const string StatusTrailer = "grpc-status";

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
    ///     Responds to a unary call with the message. The status is <see cref="StatusCode.OK" /> unless
    ///     <see cref="WithGrpcStatus" /> sets another one.
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

        if (!HasStatus(response))
        {
            response = WithStatus(response, StatusCode.OK, null, []);
        }

        return response.WithBody(WriteMessage(message));
    }

    /// <summary>
    ///     Ends the call with a gRPC status, as a service that fails the call does.
    /// </summary>
    /// <remarks>
    ///     With <paramref name="details" /> the status follows the gRPC rich error model: the client reads the code,
    ///     the message and the details as a <c>google.rpc.Status</c>, for example with <c>GetRpcStatus()</c> of the
    ///     <c>Grpc.StatusProto</c> package.
    /// </remarks>
    /// <param name="response">
    ///     The response builder.
    /// </param>
    /// <param name="statusCode">
    ///     The status code.
    /// </param>
    /// <param name="message">
    ///     The status message; the client reads it from <see cref="Status.Detail" />.
    /// </param>
    /// <param name="details">
    ///     The error details, for example <c>google.rpc.ErrorInfo</c> or a message of the service's own. Each one
    ///     is packed into <see cref="Any" /> unless it already is one.
    /// </param>
    /// <returns>
    ///     The response builder.
    /// </returns>
    public static IResponseBuilder WithGrpcStatus(this IResponseBuilder response, StatusCode statusCode,
        string? message = null, params IMessage[] details)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(details);

        return WithStatus(response, statusCode, message, details);
    }

    /// <summary>
    ///     Adds a trailer with a text value to the response; the client reads it from the trailers of the call or
    ///     from <see cref="RpcException.Trailers" />. A trailer added twice has both values.
    /// </summary>
    /// <param name="response">
    ///     The response builder.
    /// </param>
    /// <param name="key">
    ///     The trailer name. It must not end with <c>-bin</c> or start with <c>grpc-</c>.
    /// </param>
    /// <param name="value">
    ///     The value, printable ASCII characters only.
    /// </param>
    /// <returns>
    ///     The response builder.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if the name ends with <c>-bin</c> or starts with <c>grpc-</c>, or the value has characters other
    ///     than printable ASCII.
    /// </exception>
    public static IResponseBuilder WithGrpcTrailer(this IResponseBuilder response, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfReserved(key);

        if (key.EndsWith(BinarySuffix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The gRPC trailer '{key}' is binary: pass its value as bytes.", nameof(key));
        }

        if (value.Any(character => character is < ' ' or > '~'))
        {
            throw new ArgumentException(
                $"The value of the gRPC trailer '{key}' must be printable ASCII; send other values as bytes in a " +
                $"trailer whose name ends with '{BinarySuffix}'.", nameof(value));
        }

        return WithContentType(response).WithTrailingHeader(key, value);
    }

    /// <summary>
    ///     Adds a trailer with a binary value to the response; the client reads it from the trailers of the call or
    ///     from <see cref="RpcException.Trailers" />. A trailer added twice has both values.
    /// </summary>
    /// <param name="response">
    ///     The response builder.
    /// </param>
    /// <param name="key">
    ///     The trailer name. It must end with <c>-bin</c> and not start with <c>grpc-</c>.
    /// </param>
    /// <param name="value">
    ///     The value; it is sent base64-encoded, as gRPC requires for binary metadata.
    /// </param>
    /// <returns>
    ///     The response builder.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if the name does not end with <c>-bin</c> or starts with <c>grpc-</c>.
    /// </exception>
    public static IResponseBuilder WithGrpcTrailer(this IResponseBuilder response, string key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfReserved(key);

        if (!key.EndsWith(BinarySuffix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The name of a binary gRPC trailer must end with '{BinarySuffix}': '{key}'.", nameof(key));
        }

        return WithContentType(response).WithTrailingHeader(key, Convert.ToBase64String(value));
    }

    private static IResponseBuilder WithContentType(IResponseBuilder response) =>
        response.WithHeader("Content-Type", ContentType);

    private static bool HasStatus(IResponseBuilder response) =>
        Trailers(response)?.ContainsKey(StatusTrailer) == true;

    private static IDictionary<string, WireMockList<string>>? Trailers(IResponseBuilder response)
    {
        if (response is Response { ResponseMessage: { } message })
        {
            return message.TrailingHeaders;
        }

        throw new InvalidOperationException(
            $"The gRPC extensions keep the status in the trailers of WireMock's own response builder " +
            $"('{typeof(Response).FullName}' with a response message), which '{response.GetType().FullName}' is " +
            $"not. WireMock.Net version: {typeof(Response).Assembly.GetName().Version}.");
    }

    private static void ThrowIfReserved(string key)
    {
        if (key.StartsWith(ReservedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The trailer '{key}' is reserved: gRPC keeps the names that start with '{ReservedPrefix}' for " +
                $"itself. Set the status, its message and its details with {nameof(WithGrpcStatus)}.", nameof(key));
        }
    }

    private static IResponseBuilder WithStatus(IResponseBuilder response, StatusCode statusCode, string? message,
        IMessage[] details)
    {
        if (Trailers(response) is { } trailers)
        {
            trailers.Remove(StatusTrailer);
            trailers.Remove(MessageTrailer);
            trailers.Remove(DetailsTrailer);
        }

        response = WithContentType(response)
            .WithTrailingHeader(StatusTrailer, ((int)statusCode).ToString(CultureInfo.InvariantCulture));

        if (message is not null)
        {
            response = response.WithTrailingHeader(MessageTrailer, Uri.EscapeDataString(message));
        }

        return details.Length == 0
            ? response
            : response.WithTrailingHeader(DetailsTrailer,
                Convert.ToBase64String(WriteStatus(statusCode, message, details)));
    }

    private static byte[] WriteStatus(StatusCode statusCode, string? message, IMessage[] details)
    {
        using var buffer = new MemoryStream();
        using var output = new CodedOutputStream(buffer, true);

        if (statusCode != StatusCode.OK)
        {
            output.WriteTag(1, WireFormat.WireType.Varint);
            output.WriteInt32((int)statusCode);
        }

        if (!string.IsNullOrEmpty(message))
        {
            output.WriteTag(2, WireFormat.WireType.LengthDelimited);
            output.WriteString(message);
        }

        foreach (var detail in details)
        {
            ArgumentNullException.ThrowIfNull(detail, nameof(details));

            output.WriteTag(3, WireFormat.WireType.LengthDelimited);
            output.WriteMessage(detail as Any ?? Any.Pack(detail));
        }

        output.Flush();

        return buffer.ToArray();
    }

    internal static bool TryReadMessage<TMessage>(byte[]? body, out TMessage message)
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
