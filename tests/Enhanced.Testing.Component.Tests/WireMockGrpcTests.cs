using Enhanced.Testing.Component;
using Enhanced.Testing.Component.WireMock;
using Grpc.Core;
using Grpc.Net.Client;
using SampleService;
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
}
