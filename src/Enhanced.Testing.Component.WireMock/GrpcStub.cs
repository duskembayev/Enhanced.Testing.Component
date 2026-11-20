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

    public static GrpcStub For(object requestBuilder) => Stubs.GetOrCreateValue(requestBuilder);

    public static GrpcStub? Find(object? requestMatcher) =>
        requestMatcher is not null && Stubs.TryGetValue(requestMatcher, out var stub) ? stub : null;

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
