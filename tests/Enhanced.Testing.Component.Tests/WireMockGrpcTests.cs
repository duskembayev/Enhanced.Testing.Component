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
