using Enhanced.Testing.Component;
using Enhanced.Testing.Component.WireMock;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Grpc.Net.Client;
using SampleService;
using System.Reflection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Enhanced.Testing.Component.Tests;

public class WireMockGrpcTests : IAsyncLifetime
{
    private readonly WireMockHarness _greeter = new() { ConfigurationKey = "Greeter:Address", UseHttp2 = true };
    private readonly IComponent _component;
    private GrpcChannel? _channel;

    public WireMockGrpcTests()
    {
        _component = ComponentBuilder.Create<Program>()
                                     .AddHarness(_greeter)
                                     .Build();
    }

    private Greeter.GreeterClient Client => new(_channel!);

    [Fact]
    public async Task ShouldRespondToGrpcCall()
    {
        _greeter.Server
                .Given(Request.Create()
                              .UsingGrpc(Greeter.Descriptor, nameof(Greeter.GreeterClient.SayHelloAsync))
                              .WithGrpcMessage<HelloRequest>(request => request.Name == "world"))
                .RespondWith(Response.Create().WithGrpcMessage(new HelloReply { Message = "Hello from the fake" }));

        var reply = await Client.SayHelloAsync(new HelloRequest { Name = "world" });

        Assert.Equal("Hello from the fake", reply.Message);
        Assert.Single(_greeter.Server.LogEntries);
    }

    [Theory]
    [InlineData("greet.Greeter/SayHello")]
    [InlineData("/greet.Greeter/SayHello")]
    public async Task ShouldMatchGrpcMethodByFullName(string method)
    {
        _greeter.Server
                .Given(Request.Create().UsingGrpc(method))
                .RespondWith(Response.Create().WithGrpcMessage(new HelloReply { Message = "matched" }));

        var reply = await Client.SayHelloAsync(new HelloRequest { Name = "anyone" });

        Assert.Equal("matched", reply.Message);
    }

    [Fact]
    public async Task ShouldReportUnimplementedWhenGrpcMessageDoesNotMatch()
    {
        _greeter.Server
                .Given(Request.Create()
                              .UsingGrpc(Greeter.Descriptor, nameof(Greeter.GreeterClient.SayHelloAsync))
                              .WithGrpcMessage<HelloRequest>(request => request.Name == "world"))
                .RespondWith(Response.Create().WithGrpcMessage(new HelloReply { Message = "Hello" }));

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await Client.SayHelloAsync(new HelloRequest { Name = "someone else" }));

        Assert.Equal(StatusCode.Unimplemented, exception.StatusCode);
        Assert.Contains("did not satisfy the 1 stub(s)", exception.Status.Detail);
        Assert.Contains("greet.Greeter/SayHello", exception.Status.Detail);
        Assert.Contains("\"name\": \"someone else\"", exception.Status.Detail);
    }

    [Fact]
    public async Task ShouldExplainCallOfMethodWithoutStubs()
    {
        var withoutStubs = await Assert.ThrowsAsync<RpcException>(
            async () => await Client.SayHelloAsync(new HelloRequest { Name = "world" }));

        Assert.Equal(StatusCode.Unimplemented, withoutStubs.StatusCode);
        Assert.Contains("the server has no gRPC stubs", withoutStubs.Status.Detail);

        _greeter.Server
                .Given(Request.Create().UsingGrpc("greet.Greeter/SayGoodbye"))
                .RespondWith(Response.Create().WithGrpcMessage(new HelloReply { Message = "Bye" }));

        var otherMethod = await Assert.ThrowsAsync<RpcException>(
            async () => await Client.SayHelloAsync(new HelloRequest { Name = "world" }));

        Assert.Contains("the method has no stubs", otherMethod.Status.Detail);
        Assert.Contains("Stubbed methods: greet.Greeter/SayGoodbye", otherMethod.Status.Detail);
    }

    [Fact]
    public async Task ShouldExplainUnmatchedCallAfterReset()
    {
        _greeter.Reset();

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await Client.SayHelloAsync(new HelloRequest { Name = "world" }));

        Assert.Equal(StatusCode.Unimplemented, exception.StatusCode);
        Assert.Contains("No stub matched the gRPC call 'greet.Greeter/SayHello'", exception.Status.Detail);
    }

    [Fact]
    public void ShouldRejectMessageTypeThatMethodDoesNotTake()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Request.Create()
                         .UsingGrpc(Greeter.Descriptor, nameof(Greeter.GreeterClient.SayHelloAsync))
                         .WithGrpcMessage<HelloReply>(_ => true));

        Assert.Contains("greet.Greeter/SayHello", exception.Message);
        Assert.Contains("greet.HelloRequest", exception.Message);
        Assert.Contains(nameof(HelloReply), exception.Message);

        Assert.Throws<ArgumentException>(
            () => Request.Create()
                         .WithGrpcMessage<HelloReply>(_ => true)
                         .UsingGrpc(Greeter.Descriptor, nameof(Greeter.GreeterClient.SayHelloAsync)));
    }

    [Fact]
    public async Task ShouldRespondWithGrpcStatus()
    {
        _greeter.Server
                .Given(Request.Create().UsingGrpc(Greeter.Descriptor, nameof(Greeter.GreeterClient.SayHelloAsync)))
                .RespondWith(Response.Create().WithGrpcStatus(StatusCode.NotFound, "No such person: Zoë & co."));

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await Client.SayHelloAsync(new HelloRequest { Name = "Zoë" }));

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
        Assert.Equal("No such person: Zoë & co.", exception.Status.Detail);
    }

    [Fact]
    public async Task ShouldRespondWithGrpcStatusDetails()
    {
        var error = new ErrorInfo { Reason = "PERSON_BLOCKED", Domain = "greeter", Metadata = { ["name"] = "Zoë" } };
        var hint = new HelloReply { Message = "Greet someone else" };

        _greeter.Server
                .Given(Request.Create().UsingGrpc(Greeter.Descriptor, nameof(Greeter.GreeterClient.SayHelloAsync)))
                .RespondWith(Response.Create()
                                     .WithGrpcStatus(StatusCode.FailedPrecondition, "Zoë is blocked", error, hint));

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await Client.SayHelloAsync(new HelloRequest { Name = "Zoë" }));

        Assert.Equal(StatusCode.FailedPrecondition, exception.StatusCode);
        Assert.Equal("Zoë is blocked", exception.Status.Detail);

        var status = exception.GetRpcStatus();

        Assert.NotNull(status);
        Assert.Equal((int)StatusCode.FailedPrecondition, status.Code);
        Assert.Equal("Zoë is blocked", status.Message);
        Assert.Equal(2, status.Details.Count);
        Assert.Equal(error, status.GetDetail<ErrorInfo>());
        Assert.Equal(hint, status.Details[1].Unpack<HelloReply>());
    }

    [Fact]
    public async Task ShouldRespondWithPackedGrpcStatusDetails()
    {
        var expected = new Google.Rpc.Status
        {
            Code = (int)StatusCode.NotFound,
            Message = "No such person",
            Details = { Any.Pack(new ResourceInfo { ResourceType = "person", ResourceName = "Zoë" }) }
        };

        _greeter.Server
                .Given(Request.Create().UsingGrpc(Greeter.Descriptor, nameof(Greeter.GreeterClient.SayHelloAsync)))
                .RespondWith(Response.Create()
                                     .WithGrpcStatus((StatusCode)expected.Code, expected.Message,
                                         [.. expected.Details]));

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await Client.SayHelloAsync(new HelloRequest { Name = "Zoë" }));

        Assert.Equal(expected, exception.GetRpcStatus());
    }

    [Fact]
    public async Task ShouldNotSendGrpcStatusDetailsWithoutDetails()
    {
        _greeter.Server
                .Given(Request.Create().UsingGrpc(Greeter.Descriptor, nameof(Greeter.GreeterClient.SayHelloAsync)))
                .RespondWith(Response.Create().WithGrpcStatus(StatusCode.NotFound, "No such person"));

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await Client.SayHelloAsync(new HelloRequest { Name = "Zoë" }));

        Assert.Null(exception.GetRpcStatus());
    }

    [Fact]
    public async Task ShouldRespondWithGrpcTrailers()
    {
        byte[] signature = [0, 1, 2, 250, 251, 252, 253, 254, 255];

        _greeter.Server
                .Given(Request.Create().UsingGrpc(Greeter.Descriptor, nameof(Greeter.GreeterClient.SayHelloAsync)))
                .RespondWith(Response.Create()
                                     .WithGrpcTrailer("error-code", "PERSON_BLOCKED")
                                     .WithGrpcStatus(StatusCode.PermissionDenied, "Blocked")
                                     .WithGrpcTrailer("signature-bin", signature));

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await Client.SayHelloAsync(new HelloRequest { Name = "Zoë" }));

        Assert.Equal(StatusCode.PermissionDenied, exception.StatusCode);
        Assert.Equal("Blocked", exception.Status.Detail);
        Assert.Equal("PERSON_BLOCKED", exception.Trailers.GetValue("error-code"));
        Assert.Equal(signature, exception.Trailers.GetValueBytes("signature-bin"));
    }

    [Fact]
    public async Task ShouldSendGrpcTrailersAfterMessage()
    {
        _greeter.Server
                .Given(Request.Create().UsingGrpc(Greeter.Descriptor, nameof(Greeter.GreeterClient.SayHelloAsync)))
                .RespondWith(Response.Create()
                                     .WithGrpcMessage(new HelloReply { Message = "Hello" })
                                     .WithGrpcTrailer("served-by", "fake")
                                     .WithGrpcTrailer("served-by", "wiremock"));

        using var call = Client.SayHelloAsync(new HelloRequest { Name = "world" });

        var reply = await call;

        Assert.Equal("Hello", reply.Message);
        Assert.Equal(StatusCode.OK, call.GetStatus().StatusCode);
        Assert.Equal(["fake", "wiremock"],
            call.GetTrailers().GetAll("served-by").Select(entry => entry.Value).Order().ToArray());
    }

    [Fact]
    public async Task ShouldKeepGrpcStatusSetBeforeMessage()
    {
        _greeter.Server
                .Given(Request.Create().UsingGrpc(Greeter.Descriptor, nameof(Greeter.GreeterClient.SayHelloAsync)))
                .RespondWith(Response.Create()
                                     .WithGrpcStatus(StatusCode.DataLoss, "Lost")
                                     .WithGrpcMessage(new HelloReply { Message = "Hello" }));

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await Client.SayHelloAsync(new HelloRequest { Name = "world" }));

        Assert.Equal(StatusCode.DataLoss, exception.StatusCode);
    }

    [Fact]
    public void ShouldRejectGrpcTrailerOfWrongKind()
    {
        Assert.Throws<ArgumentException>(() => Response.Create().WithGrpcTrailer("signature-bin", "text"));
        Assert.Throws<ArgumentException>(() => Response.Create().WithGrpcTrailer("signature", [1, 2, 3]));
        Assert.Throws<ArgumentException>(() => Response.Create().WithGrpcTrailer("error-code", "Zoë"));
    }

    [Theory]
    [InlineData("grpc-status")]
    [InlineData("grpc-message")]
    [InlineData("Grpc-Encoding")]
    public void ShouldRejectReservedGrpcTrailer(string key)
    {
        var exception = Assert.Throws<ArgumentException>(() => Response.Create().WithGrpcTrailer(key, "5"));

        Assert.Contains(nameof(WireMockGrpcExtensions.WithGrpcStatus), exception.Message);

        Assert.Throws<ArgumentException>(
            () => Response.Create().WithGrpcTrailer("grpc-status-details-bin", [1, 2, 3]));
    }

    [Fact]
    public async Task ShouldSendLastGrpcStatus()
    {
        _greeter.Server
                .Given(Request.Create().UsingGrpc(Greeter.Descriptor, nameof(Greeter.GreeterClient.SayHelloAsync)))
                .RespondWith(Response.Create()
                                     .WithGrpcStatus(StatusCode.Aborted, "First",
                                         new ErrorInfo { Reason = "FIRST" })
                                     .WithGrpcTrailer("served-by", "fake")
                                     .WithGrpcStatus(StatusCode.NotFound, "Second"));

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await Client.SayHelloAsync(new HelloRequest { Name = "Zoë" }));

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
        Assert.Equal("Second", exception.Status.Detail);
        Assert.Null(exception.GetRpcStatus());
        Assert.Equal("fake", exception.Trailers.GetValue("served-by"));
    }

    [Fact]
    public void ShouldRejectResponseBuilderOfAnotherType()
    {
        var builder = DispatchProxy.Create<IResponseBuilder, OtherResponseBuilder>();

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.WithGrpcStatus(StatusCode.NotFound));

        Assert.Contains(typeof(Response).FullName!, exception.Message);
        Assert.Contains(typeof(Response).Assembly.GetName().Version!.ToString(), exception.Message);
        Assert.Throws<InvalidOperationException>(() => builder.WithGrpcMessage(new HelloReply()));
    }

    [Fact]
    public async Task ShouldMatchGrpcMethodByNameFromProto()
    {
        _greeter.Server
                .Given(Request.Create().UsingGrpc(Greeter.Descriptor, nameof(Greeter.GreeterBase.SayHello)))
                .RespondWith(Response.Create().WithGrpcMessage(new HelloReply { Message = "matched" }));

        var reply = await Client.SayHelloAsync(new HelloRequest { Name = "anyone" });

        Assert.Equal("matched", reply.Message);
    }

    [Fact]
    public void ShouldRejectUnknownGrpcMethod()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Request.Create().UsingGrpc(Greeter.Descriptor, "SayGoodbye"));

        Assert.Contains("greet.Greeter", exception.Message);
        Assert.Contains("SayGoodbye", exception.Message);
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _component.StartAsync(default);
        _channel = GrpcChannel.ForAddress(_greeter.BaseAddress);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        _channel?.Dispose();
        await _component.StopAsync(default);
    }

    public class OtherResponseBuilder : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }
}
