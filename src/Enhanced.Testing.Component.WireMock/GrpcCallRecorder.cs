using Grpc.Core;

namespace Enhanced.Testing.Component.WireMock;

internal sealed class GrpcCallRecorder : CallInvoker
{
    private readonly List<GrpcRecordedCall> _calls = [];

    public IReadOnlyList<GrpcRecordedCall> Record(Action call)
    {
        lock (_calls)
        {
            _calls.Clear();
            call();

            return _calls.ToArray();
        }
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        _calls.Add(new GrpcRecordedCall(method.ServiceName, method.Name, request));

        return new AsyncUnaryCall<TResponse>(
            Task.FromResult<TResponse>(null!), Task.FromResult(new Metadata()), () => Status.DefaultSuccess,
            () => [], () => { });
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        throw NotUnary(method);
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        throw NotUnary(method);
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options)
    {
        throw NotUnary(method);
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options)
    {
        throw NotUnary(method);
    }

    private static NotSupportedException NotUnary(IMethod method) =>
        new($"The gRPC call '{method.ServiceName}/{method.Name}' cannot be stubbed this way: name a unary method " +
            $"through the asynchronous method of the client, for example 'client => client.{method.Name}Async(null)'.");
}

internal sealed record GrpcRecordedCall(string Service, string Method, object? Request);
