using Enhanced.Testing.Component;
using Enhanced.Testing.Component.WireMock;
using Google.Protobuf;
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

    [Fact]
    public async Task ShouldRespondToServerStreamingCall()
    {
        _greeter.Server
                .Given(Request.Create()
                              .UsingGrpc("greet.Greeter/SayHellos")
                              .WithGrpcMessage<HelloRequest>(request => request.Name == "world"))
                .RespondWith(Response.Create().WithGrpcMessages(
                    new HelloReply { Message = "first" },
                    new HelloReply { Message = "second" },
                    new HelloReply { Message = "third" }));

        using var call = Invoker.AsyncServerStreamingCall(
            Method(MethodType.ServerStreaming, "SayHellos"), null, default, new HelloRequest { Name = "world" });

        var replies = new List<string>();

        await foreach (var reply in call.ResponseStream.ReadAllAsync())
        {
            replies.Add(reply.Message);
        }

        Assert.Equal(["first", "second", "third"], replies);
        Assert.Equal(StatusCode.OK, call.GetStatus().StatusCode);
    }

    [Fact]
    public async Task ShouldFailServerStreamingCallAfterMessages()
    {
        _greeter.Server
                .Given(Request.Create().UsingGrpc("greet.Greeter/SayHellos"))
                .RespondWith(Response.Create()
                                     .WithGrpcMessages(new HelloReply { Message = "first" })
                                     .WithGrpcStatus(StatusCode.Aborted, "The stream broke."));

        using var call = Invoker.AsyncServerStreamingCall(
            Method(MethodType.ServerStreaming, "SayHellos"), null, default, new HelloRequest { Name = "world" });

        Assert.True(await call.ResponseStream.MoveNext());
        Assert.Equal("first", call.ResponseStream.Current.Message);

        var exception = await Assert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext());

        Assert.Equal(StatusCode.Aborted, exception.StatusCode);
        Assert.Equal("The stream broke.", exception.Status.Detail);
    }

    [Fact]
    public async Task ShouldRespondToClientStreamingCall()
    {
        _greeter.Server
                .Given(Request.Create()
                              .UsingGrpc("greet.Greeter/CollectHellos")
                              .WithGrpcMessages<HelloRequest>(
                                  requests => requests.Select(request => request.Name).SequenceEqual(["a", "b", "c"])))
                .RespondWith(Response.Create().WithGrpcMessage(new HelloReply { Message = "three names" }));

        using var call = Invoker.AsyncClientStreamingCall(
            Method(MethodType.ClientStreaming, "CollectHellos"), null, default);

        foreach (var name in new[] { "a", "b", "c" })
        {
            await call.RequestStream.WriteAsync(new HelloRequest { Name = name });
        }

        await call.RequestStream.CompleteAsync();

        Assert.Equal("three names", (await call).Message);
    }

    [Fact]
    public async Task ShouldRespondToDuplexCallAfterClientCompletes()
    {
        _greeter.Server
                .Given(Request.Create()
                              .UsingGrpc("greet.Greeter/Chat")
                              .WithGrpcMessages<HelloRequest>(requests => requests.Count == 2))
                .RespondWith(Response.Create().WithGrpcMessages(
                    new HelloReply { Message = "first" },
                    new HelloReply { Message = "second" }));

        using var call = Invoker.AsyncDuplexStreamingCall(Method(MethodType.DuplexStreaming, "Chat"), null, default);

        await call.RequestStream.WriteAsync(new HelloRequest { Name = "a" });
        await call.RequestStream.WriteAsync(new HelloRequest { Name = "b" });
        await call.RequestStream.CompleteAsync();

        var replies = new List<string>();

        await foreach (var reply in call.ResponseStream.ReadAllAsync())
        {
            replies.Add(reply.Message);
        }

        Assert.Equal(["first", "second"], replies);
    }

    [Fact]
    public async Task ShouldRespondToServerStreamingCallWithoutMessages()
    {
        _greeter.Server
                .Given(Request.Create().UsingGrpc("greet.Greeter/SayHellos"))
                .RespondWith(Response.Create().WithGrpcMessages());

        using var call = Invoker.AsyncServerStreamingCall(
            Method(MethodType.ServerStreaming, "SayHellos"), null, default, new HelloRequest { Name = "world" });

        Assert.False(await call.ResponseStream.MoveNext());
    }

    private CallInvoker Invoker => _channel!.CreateCallInvoker();

    private static Method<HelloRequest, HelloReply> Method(MethodType type, string name) =>
        new(type, "greet.Greeter", name,
            Marshallers.Create(message => message.ToByteArray(), HelloRequest.Parser.ParseFrom),
            Marshallers.Create(message => message.ToByteArray(), HelloReply.Parser.ParseFrom));

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
