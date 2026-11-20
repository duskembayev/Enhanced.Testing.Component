using Enhanced.Testing.Component;
using Enhanced.Testing.Component.WireMock;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SampleService;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Enhanced.Testing.Component.Tests;

public class WireMockServiceDiscoveryTests : IAsyncLifetime
{
    private readonly WireMockHarness _quotes = new() { ServiceName = "quotes", ConfigurationKey = "QuotesApi:BaseUrl" };
    private readonly WireMockHarness _payments = new() { ServiceName = "payments", EndpointName = "api" };
    private readonly WireMockHarness _greeter = new() { ServiceName = "greeter", EndpointName = "grpc", UseHttp2 = true };
    private readonly IComponent _component;

    public WireMockServiceDiscoveryTests()
    {
        _component = ComponentBuilder.Create<Program>()
                                     .AddHarness(_quotes)
                                     .AddHarness(_payments)
                                     .AddHarness(_greeter)
                                     .Build();
    }

    private IConfiguration Configuration => _component.Services.GetRequiredService<IConfiguration>();

    [Fact]
    public void ShouldPassServerAddressAsServiceEndpoint()
    {
        Assert.Equal(_quotes.Server.Url, Configuration["Services:quotes:http:0"]);
        Assert.Equal(_quotes.Server.Url, Configuration["QuotesApi:BaseUrl"]);
        Assert.Equal(_payments.Server.Url, Configuration["Services:payments:api:0"]);
        Assert.Equal(_greeter.Server.Url, Configuration["Services:greeter:grpc:0"]);
    }

    [Theory]
    [InlineData("http://quotes")]
    [InlineData("https+http://quotes")]
    public async Task ShouldResolveServiceByName(string address)
    {
        _quotes.Server
               .Given(Request.Create().WithPath("/quote").UsingGet())
               .RespondWith(Response.Create().WithBody("Stay hungry"));

        await using var services = CreateServices(
            collection => collection.AddHttpClient("quotes", client => client.BaseAddress = new Uri(address)));

        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("quotes");

        Assert.Equal("Stay hungry", await client.GetStringAsync("/quote"));
    }

    [Fact]
    public async Task ShouldResolveNamedEndpoint()
    {
        _payments.Server
                 .Given(Request.Create().WithPath("/status").UsingGet())
                 .RespondWith(Response.Create().WithBody("paid"));

        await using var services = CreateServices(
            collection => collection.AddHttpClient("payments",
                client => client.BaseAddress = new Uri("http://_api.payments")));

        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("payments");

        Assert.Equal("paid", await client.GetStringAsync("/status"));
    }

    [Fact]
    public async Task ShouldResolveGrpcService()
    {
        _greeter.Server
                .Given(Request.Create().UsingGrpc<Greeter.GreeterClient>(nameof(Greeter.GreeterClient.SayHelloAsync)))
                .RespondWith(Response.Create().WithGrpcMessage(new HelloReply { Message = "Hello from the fake" }));

        await using var services = CreateServices(
            collection => collection.AddGrpcClient<Greeter.GreeterClient>(
                options => options.Address = new Uri("http://_grpc.greeter")));

        var reply = await services.GetRequiredService<Greeter.GreeterClient>()
                                  .SayHelloAsync(new HelloRequest { Name = "world" });

        Assert.Equal("Hello from the fake", reply.Message);
    }

    [Fact]
    public void ShouldRejectEmptyServiceAndEndpointNames()
    {
        Assert.Throws<ArgumentException>(() => new WireMockHarness { ServiceName = " " });
        Assert.Throws<ArgumentException>(() => new WireMockHarness { EndpointName = "" });
        Assert.Null(new WireMockHarness().ServiceName);
        Assert.Equal("http", new WireMockHarness().EndpointName);
    }

    private ServiceProvider CreateServices(Action<IServiceCollection> addClient)
    {
        var services = new ServiceCollection();

        services.AddSingleton(Configuration);
        services.AddServiceDiscovery();
        services.ConfigureHttpClientDefaults(http => http.AddServiceDiscovery());
        addClient(services);

        return services.BuildServiceProvider();
    }

    Task IAsyncLifetime.InitializeAsync() => _component.StartAsync(default);

    Task IAsyncLifetime.DisposeAsync() => _component.StopAsync(default);
}
