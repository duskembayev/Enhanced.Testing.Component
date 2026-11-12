using Enhanced.Testing.Component;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SampleService.Services;

namespace SampleService.Tests;

public class ComponentBuilderTests
{
    [Fact]
    public async Task ShouldKeepHostedServicesByDefault()
    {
        var hostedServices = await GetHostedServiceTypesAsync(ComponentBuilder.Create<Program>());

        Assert.Contains(typeof(HeartbeatService), hostedServices);
    }

    [Fact]
    public async Task ShouldRemoveHostedServiceRegisteredByApplication()
    {
        var hostedServices = await GetHostedServiceTypesAsync(ComponentBuilder.Create<Program>()
                                                                              .RemoveHostedService<HeartbeatService>());

        Assert.DoesNotContain(typeof(HeartbeatService), hostedServices);
    }

    [Fact]
    public async Task ShouldRemoveInstanceAndFactoryRegistrationsOfHostedService()
    {
        var builder = ComponentBuilder.Create<Program>()
                                      .Configure(webHost => webHost.ConfigureServices(services =>
                                      {
                                          services.AddHostedService(_ => new NoopHostedService());
                                          services.AddSingleton<IHostedService>(new NoopHostedService());
                                          services.AddHostedService<OtherHostedService>();
                                      }))
                                      .RemoveHostedService<NoopHostedService>();

        var hostedServices = await GetHostedServiceTypesAsync(builder);

        Assert.DoesNotContain(typeof(NoopHostedService), hostedServices);
        Assert.Contains(typeof(OtherHostedService), hostedServices);
        Assert.Contains(typeof(HeartbeatService), hostedServices);
    }

    [Fact]
    public async Task ShouldKeepKeyedAndLaterRegistrationsOfHostedService()
    {
        var builder = ComponentBuilder.Create<Program>()
                                      .Configure(webHost => webHost.ConfigureServices(services =>
                                          services.AddKeyedSingleton<IHostedService, NoopHostedService>("keyed")))
                                      .RemoveHostedService<NoopHostedService>()
                                      .Configure(webHost => webHost.ConfigureServices(services =>
                                          services.AddHostedService<NoopHostedService>()));

        var component = builder.Build();

        try
        {
            await component.StartAsync(default);

            Assert.IsType<NoopHostedService>(component.Services.GetRequiredKeyedService<IHostedService>("keyed"));
            Assert.Contains(component.Services.GetServices<IHostedService>(), service => service is NoopHostedService);
        }
        finally
        {
            await component.StopAsync(default);
        }
    }

    [Fact]
    public async Task ShouldIgnoreHostedServiceThatIsNotRegistered()
    {
        var hostedServices = await GetHostedServiceTypesAsync(ComponentBuilder.Create<Program>()
                                                                              .RemoveHostedService<NoopHostedService>());

        Assert.Contains(typeof(HeartbeatService), hostedServices);
    }

    [Fact]
    public async Task ShouldHostApplicationThroughCustomFactory()
    {
        await using var factory = new CustomApplicationFactory();
        var httpClient = new HttpClientHarness();
        var component = ComponentBuilder.Create(factory)
                                        .Configure(webHost => webHost.UseSetting("Sample:Builder", "builder"))
                                        .AddHarness(httpClient)
                                        .Build();

        try
        {
            await component.StartAsync(default);

            var configuration = component.Services.GetRequiredService<IConfiguration>();
            Assert.Equal("factory", configuration["Sample:Factory"]);
            Assert.Equal("builder", configuration["Sample:Builder"]);
            Assert.True(factory.HostCreated);

            var response = await httpClient.CreateClient().GetAsync("/health");
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            await component.StopAsync(default);
        }

        Assert.False(factory.Disposed);
    }

    [Fact]
    public async Task ShouldCreateComponentsFromSameFactory()
    {
        await using var factory = new CustomApplicationFactory();

        for (var i = 0; i < 2; i++)
        {
            var httpClient = new HttpClientHarness();
            var component = ComponentBuilder.Create(factory).AddHarness(httpClient).Build();

            try
            {
                await component.StartAsync(default);

                var response = await httpClient.CreateClient().GetAsync("/health");
                response.EnsureSuccessStatusCode();
            }
            finally
            {
                await component.StopAsync(default);
            }
        }

        Assert.False(factory.Disposed);
    }

    [Fact]
    public void ShouldRejectNullFactory()
    {
        Assert.Throws<ArgumentNullException>(() => ComponentBuilder.Create<Program>(null!));
    }

    private static async Task<Type[]> GetHostedServiceTypesAsync(IComponentBuilder builder)
    {
        var component = builder.Build();

        try
        {
            await component.StartAsync(default);
            return component.Services.GetServices<IHostedService>().Select(service => service.GetType()).ToArray();
        }
        finally
        {
            await component.StopAsync(default);
        }
    }

    private sealed class CustomApplicationFactory : WebApplicationFactory<Program>
    {
        public bool HostCreated { get; private set; }

        public bool Disposed { get; private set; }

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return base.DisposeAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("Sample:Factory", "factory");

        protected override IHost CreateHost(IHostBuilder builder)
        {
            HostCreated = true;
            return base.CreateHost(builder);
        }
    }

    private sealed class NoopHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class OtherHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
