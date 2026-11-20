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
/// <remarks>
///     The server answers a call when the client has completed its request, so it can fake unary, server streaming
///     and client streaming calls, and duplex calls in which the client sends everything before it reads.
/// </remarks>
public static class WireMockGrpcExtensions
{
    private const string ContentType = "application/grpc";
    private const string AsyncSuffix = "Async";
    private const int MessagePrefixLength = 5;
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
        stub.DescribeRequest ??= body => TryReadFrames(body, out var frames) ? Format(parser, frames) : null;
        stub.ThrowIfRequestTypeDoesNotFit();

        return request;
    }

    /// <summary>
    ///     Matches calls whose request message satisfies the predicate. A call that sent no message or several
    ///     messages does not match.
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
        ArgumentNullException.ThrowIfNull(predicate);

        return request.WithGrpcMessages<TRequest>(messages => messages.Count == 1 && predicate(messages[0]));
    }

    /// <summary>
    ///     Matches client streaming calls by all the messages the client sent before it completed the request
    ///     stream.
    /// </summary>
    /// <param name="request">
    ///     The request builder.
    /// </param>
    /// <param name="predicate">
    ///     The condition the request messages must satisfy, in the order they were sent.
    /// </param>
    /// <typeparam name="TRequest">
    ///     The generated request message type.
    /// </typeparam>
    /// <returns>
    ///     The request builder.
    /// </returns>
    public static IRequestBuilder WithGrpcMessages<TRequest>(this IRequestBuilder request,
        Func<IReadOnlyList<TRequest>, bool> predicate)
        where TRequest : IMessage<TRequest>, new()
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(predicate);

        var stub = GrpcStub.For(request);

        stub.RequestType = typeof(TRequest);
        stub.DescribeRequest = body => TryReadMessages<TRequest>(body, out var messages) ? Format(messages) : null;
        stub.ThrowIfRequestTypeDoesNotFit();

        return request.WithBody(body => TryReadMessages<TRequest>(body, out var messages) && predicate(messages));
    }

    /// <summary>
    ///     Responds with the message and the <see cref="StatusCode.OK" /> status.
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
        ArgumentNullException.ThrowIfNull(message);

        return response.WithGrpcMessages(message);
    }

    /// <summary>
    ///     Responds to a server streaming call with the messages, in this order, and the
    ///     <see cref="StatusCode.OK" /> status. Follow it with <see cref="WithGrpcStatus" /> to fail the call after
    ///     the messages.
    /// </summary>
    /// <remarks>
    ///     The messages are sent together, when the client has completed its request.
    /// </remarks>
    /// <param name="response">
    ///     The response builder.
    /// </param>
    /// <param name="messages">
    ///     The response messages.
    /// </param>
    /// <returns>
    ///     The response builder.
    /// </returns>
    public static IResponseBuilder WithGrpcMessages(this IResponseBuilder response, params IMessage[] messages)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(messages);

        return WithStatus(response, StatusCode.OK, null).WithBody(WriteMessages(messages));
    }

    /// <summary>
    ///     Ends the call with a gRPC status, as a service that fails the call does. Without messages the client gets
    ///     only the status; after <see cref="WithGrpcMessages" /> it gets the messages first.
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

        return WithStatus(response, statusCode, detail);
    }

    private static IResponseBuilder WithStatus(IResponseBuilder response, StatusCode statusCode, string? detail)
    {
        if (response is Response { ResponseMessage.TrailingHeaders: { } trailers })
        {
            trailers.Remove(StatusTrailer);
            trailers.Remove(MessageTrailer);
        }

        response = response.WithHeader("Content-Type", ContentType)
                           .WithTrailingHeader(StatusTrailer, ((int)statusCode).ToString(CultureInfo.InvariantCulture));

        return detail is null ? response : response.WithTrailingHeader(MessageTrailer, Uri.EscapeDataString(detail));
    }

    private static bool TryReadMessages<TMessage>(byte[]? body, out IReadOnlyList<TMessage> messages)
        where TMessage : IMessage<TMessage>, new()
    {
        var result = new List<TMessage>();

        messages = result;

        if (!TryReadFrames(body, out var frames))
        {
            return false;
        }

        try
        {
            foreach (var frame in frames)
            {
                var message = new TMessage();

                message.MergeFrom(frame);
                result.Add(message);
            }
        }
        catch (InvalidProtocolBufferException)
        {
            return false;
        }

        return true;
    }

    private static bool TryReadFrames(byte[]? body, out List<byte[]> frames)
    {
        var rest = body.AsSpan();

        frames = [];

        while (!rest.IsEmpty)
        {
            if (rest.Length < MessagePrefixLength || rest[0] != 0)
            {
                return false;
            }

            var length = BinaryPrimitives.ReadUInt32BigEndian(rest.Slice(1, 4));

            if (length > rest.Length - MessagePrefixLength)
            {
                return false;
            }

            frames.Add(rest.Slice(MessagePrefixLength, (int)length).ToArray());
            rest = rest.Slice(MessagePrefixLength + (int)length);
        }

        return true;
    }

    private static string? Format(MessageParser parser, List<byte[]> frames)
    {
        try
        {
            return Format(frames.ConvertAll(parser.ParseFrom));
        }
        catch (InvalidProtocolBufferException)
        {
            return null;
        }
    }

    private static string Format<TMessage>(IReadOnlyList<TMessage> messages)
        where TMessage : IMessage
    {
        var formatted = messages.Select(message => JsonFormatter.Default.Format(message)).ToList();

        return formatted.Count == 1 ? formatted[0] : $"[{string.Join(", ", formatted)}]";
    }

    private static byte[] WriteMessages(IMessage[] messages)
    {
        var sizes = Array.ConvertAll(messages, message => message.CalculateSize());
        var body = new byte[sizes.Sum() + (MessagePrefixLength * messages.Length)];
        var rest = body.AsSpan();

        for (var i = 0; i < messages.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(rest.Slice(1, 4), (uint)sizes[i]);
            messages[i].WriteTo(rest.Slice(MessagePrefixLength, sizes[i]));
            rest = rest.Slice(MessagePrefixLength + sizes[i]);
        }

        return body;
    }
}
