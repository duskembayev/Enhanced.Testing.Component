using System.Net;
using Enhanced.Testing.Component;
using Enhanced.Testing.Component.WireMock;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace SampleService.Tests;

public class WireMockHarnessTests : IAsyncLifetime
{
    private readonly HttpClientHarness _httpClient = new();
    private readonly WireMockHarness _quotesApi = new() { ConfigurationKey = "QuotesApi:BaseUrl" };
    private readonly IComponent _component;

    public WireMockHarnessTests()
    {
        _component = ComponentBuilder.Create<Program>()
                                     .AddHarness(_httpClient)
                                     .AddHarness(_quotesApi)
                                     .Build();
    }

    [Fact]
    public void ShouldPassBaseUrlToConfiguration()
    {
        var configuration = _component.Services.GetRequiredService<IConfiguration>();

        Assert.Equal(_quotesApi.Server.Url, configuration["QuotesApi:BaseUrl"]);
        Assert.Equal(new Uri(_quotesApi.Server.Url!), _quotesApi.BaseAddress);
    }

    [Fact]
    public async Task ShouldReturnStubbedResponse()
    {
        _quotesApi.Server
                  .Given(Request.Create().WithPath("/quote").UsingGet())
                  .RespondWith(Response.Create().WithBody("Stay hungry, stay foolish."));

        var quote = await _httpClient.CreateClient().GetStringAsync("/quote");

        Assert.Equal("Stay hungry, stay foolish.", quote);
        var entry = Assert.Single(_quotesApi.Server.LogEntries);
        Assert.Equal("/quote", entry.RequestMessage?.Path);
    }

    [Fact]
    public async Task ShouldRemoveStubsOnReset()
    {
        _quotesApi.Server
                  .Given(Request.Create().WithPath("/quote").UsingGet())
                  .RespondWith(Response.Create().WithBody("quote"));

        using var client = _quotesApi.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/quote")).StatusCode);

        _quotesApi.Reset();

        Assert.Empty(_quotesApi.Server.Mappings);
        Assert.Empty(_quotesApi.Server.LogEntries);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/quote")).StatusCode);
    }

    [Fact]
    public async Task ShouldStopServerWithComponent()
    {
        var harness = new WireMockHarness { ConfigureServer = settings => settings.StartAdminInterface = true };
        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(harness)
                                        .Build();

        await component.StartAsync(default);
        using var client = harness.CreateClient();
        var adminResponse = await client.GetAsync("/__admin/mappings");
        await component.StopAsync(default);
        await component.StopAsync(default);

        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        Assert.Throws<InvalidOperationException>(() => harness.Server);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("/__admin/mappings"));
    }

    [Fact]
    public async Task ShouldStartNewServerWhenReusedByAnotherComponent()
    {
        var harness = new WireMockHarness { ConfigurationKey = "QuotesApi:BaseUrl" };
        var first = ComponentBuilder.Create<Program>().AddHarness(harness).Build();
        await first.StartAsync(default);
        harness.Server
               .Given(Request.Create().WithPath("/quote").UsingGet())
               .RespondWith(Response.Create().WithBody("quote"));
        await first.StopAsync(default);

        var second = ComponentBuilder.Create<Program>().AddHarness(harness).Build();
        await second.StartAsync(default);

        try
        {
            var configuration = second.Services.GetRequiredService<IConfiguration>();
            Assert.Equal(harness.Server.Url, configuration["QuotesApi:BaseUrl"]);
            Assert.Empty(harness.Server.Mappings);
        }
        finally
        {
            await second.StopAsync(default);
        }
    }

    [Fact]
    public void ShouldFailClearlyWhenConfiguredBeforeStart()
    {
        var harness = new WireMockHarness { ConfigurationKey = "QuotesApi:BaseUrl" };

        var exception = Assert.Throws<InvalidOperationException>(() => harness.OnConfigure(null!));

        Assert.Contains("QuotesApi:BaseUrl", exception.Message);
    }

    Task IAsyncLifetime.InitializeAsync() => _component.StartAsync(default);

    Task IAsyncLifetime.DisposeAsync() => _component.StopAsync(default);
}
