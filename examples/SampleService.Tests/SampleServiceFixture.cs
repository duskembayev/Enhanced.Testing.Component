using Enhanced.Testing.Component;
using Enhanced.Testing.Component.GrpcClient;

namespace SampleService.Tests;

public class SampleServiceFixture : IAsyncLifetime
{
    private readonly IComponent _component;

    public SampleServiceFixture()
    {
        HttpClient = new HttpClientHarness();
        GrpcClient = new GrpcClientHarness();

        _component = ComponentBuilder.Create<Program>()
                                     .AddHarness(HttpClient)
                                     .AddHarness(GrpcClient)
                                     .Build();
    }

    public HttpClientHarness HttpClient { get; }

    public GrpcClientHarness GrpcClient { get; }

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _component.StartAsync(default);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _component.StopAsync(default);
    }
}
