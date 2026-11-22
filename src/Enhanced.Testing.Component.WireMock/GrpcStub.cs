using System.Runtime.CompilerServices;
using Google.Protobuf.Reflection;

namespace Enhanced.Testing.Component.WireMock;

internal sealed class GrpcStub
{
    private static readonly ConditionalWeakTable<object, GrpcStub> Stubs = new();

    public string? Method { get; set; }

    public MessageDescriptor? RequestDescriptor { get; set; }

    public Type? RequestType { get; set; }

    public Func<byte[]?, string?>? DescribeRequest { get; set; }

    public List<Func<byte[]?, bool>> Conditions { get; } = [];

    public static GrpcStub For(object requestBuilder) => Stubs.GetOrCreateValue(requestBuilder);

    public static GrpcStub? Find(object? requestMatcher) =>
        requestMatcher is not null && Stubs.TryGetValue(requestMatcher, out var stub) ? stub : null;

    public bool Satisfies(byte[]? body)
    {
        try
        {
            return Conditions.TrueForAll(condition => condition(body));
        }
        catch (Exception)
        {
            return false;
        }
    }

    public Exception? FindFailure(byte[]? body)
    {
        try
        {
            Conditions.ForEach(condition => condition(body));
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    public void ThrowIfRequestTypeDoesNotFit()
    {
        if (RequestDescriptor is not null && RequestType is not null && RequestDescriptor.ClrType != RequestType)
        {
            throw new ArgumentException(
                $"The gRPC method '{Method}' takes '{RequestDescriptor.FullName}' messages " +
                $"({RequestDescriptor.ClrType.Name}), not {RequestType.Name}.");
        }
    }
}
