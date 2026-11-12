using System.Reflection;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Enhanced.Testing.Component;
using Enhanced.Testing.Component.Redis;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.Redis;

namespace SampleService.Tests;

public class LifecycleTests
{
    [Fact]
    public async Task ShouldRemoveContainersOnStop()
    {
        var redis = new RedisHarness();
        var httpClient = new HttpClientHarness();
        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(redis)
                                        .AddHarness(httpClient)
                                        .Build();

        string containerId;

        try
        {
            await component.StartAsync(default);
            containerId = redis.Container.Id;
            Assert.True(await ContainerExistsAsync(containerId));
        }
        finally
        {
            await component.StopAsync(default);
        }

        Assert.False(await ContainerExistsAsync(containerId));
        Assert.Throws<InvalidOperationException>(() => redis.Container);
        Assert.Throws<InvalidOperationException>(() => httpClient.CreateClient());
        Assert.Throws<InvalidOperationException>(() => component.Services);
    }

    [Fact]
    public async Task ShouldRemoveContainersWhenDependencyFailsToStop()
    {
        var first = new RedisHarness();
        var second = new RedisHarness();
        var failure = new InvalidOperationException("Stop failed.");
        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(first)
                                        .AddDependency(new DelegateDependency(onStop: () => throw failure))
                                        .AddHarness(second)
                                        .Build();

        string[] containerIds = [];

        try
        {
            await component.StartAsync(default);
            containerIds = [first.Container.Id, second.Container.Id];
        }
        finally
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => component.StopAsync(default));
            Assert.Same(failure, exception);
        }

        foreach (var id in containerIds)
        {
            Assert.False(await ContainerExistsAsync(id));
        }
    }

    [Fact]
    public async Task ShouldRemoveContainersWhenStartFails()
    {
        var redis = new RedisHarness();
        string? containerId = null;
        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(redis)
                                        .AddDependency(new DelegateDependency(onStart: () =>
                                        {
                                            containerId = redis.Container.Id;
                                            throw new InvalidOperationException("Start failed.");
                                        }))
                                        .Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => component.StartAsync(default));

        Assert.NotNull(containerId);
        Assert.False(await ContainerExistsAsync(containerId));
        Assert.Throws<InvalidOperationException>(() => redis.Container);

        await component.StopAsync(default);
    }

    [Fact]
    public async Task ShouldShutDownApplicationWhileBackingServicesAreAlive()
    {
        var redis = new RedisHarness();
        TestcontainersStates? stateOnAppShutdown = null;
        Exception? accessDuringStop = null;
        var component = ComponentBuilder.Create<Program>()
                                        .AddDependency(new DelegateDependency(onStopComponent: c =>
                                            accessDuringStop = Record.Exception(() => c.Services)))
                                        .AddHarness(redis)
                                        .Configure(builder => builder.ConfigureServices(services =>
                                            services.AddSingleton<IHostedService>(
                                                new ShutdownProbe(() => stateOnAppShutdown = redis.Container.State))))
                                        .Build();

        try
        {
            await component.StartAsync(default);
        }
        finally
        {
            await component.StopAsync(default);
        }

        Assert.Equal(TestcontainersStates.Running, stateOnAppShutdown);
        Assert.IsType<InvalidOperationException>(accessDuringStop);
    }

    [Fact]
    public async Task ShouldSurfaceCancelledStopAndKeepCleanupRetryable()
    {
        var redis = new RedisHarness();
        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(redis)
                                        .Build();

        string containerId;

        try
        {
            await component.StartAsync(default);
            containerId = redis.Container.Id;

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(() => component.StopAsync(cts.Token));
            Assert.True(await ContainerExistsAsync(containerId));
        }
        finally
        {
            await component.StopAsync(default);
        }

        Assert.False(await ContainerExistsAsync(containerId));
    }

    [Fact]
    public async Task ShouldRetryContainerRemovalAfterFailure()
    {
        var harness = new FlakyRedisHarness();
        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(harness)
                                        .Build();

        string containerId;

        try
        {
            await component.StartAsync(default);
            containerId = harness.Container.Id;

            await Assert.ThrowsAsync<IOException>(() => component.StopAsync(default));

            Assert.True(await ContainerExistsAsync(containerId));
            Assert.Equal(containerId, harness.Container.Id);
        }
        finally
        {
            await component.StopAsync(default);
        }

        Assert.False(await ContainerExistsAsync(containerId));
        Assert.Throws<InvalidOperationException>(() => harness.Container);
    }

    [Fact]
    public async Task ShouldFailClearlyAfterStopAndAllowRepeatedStop()
    {
        var redis = new RedisHarness();
        var redisDatabase = new RedisDatabaseHarness(redis);
        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(redis)
                                        .AddHarness(redisDatabase)
                                        .Build();

        try
        {
            await component.StartAsync(default);
            await redisDatabase.Database.StringSetAsync("key", "value");
        }
        finally
        {
            await component.StopAsync(default);
        }

        await component.StopAsync(default);

        Assert.Throws<InvalidOperationException>(() => redisDatabase.Database);
        Assert.Throws<InvalidOperationException>(() => redisDatabase.Multiplexer);
    }

    [Fact]
    public async Task ShouldReuseHarnessAfterComponentStops()
    {
        var httpClient = new HttpClientHarness();

        foreach (var _ in Enumerable.Range(0, 2))
        {
            var component = ComponentBuilder.Create<Program>()
                                            .AddHarness(httpClient)
                                            .Build();

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
    }

    private static async Task<bool> ContainerExistsAsync(string id)
    {
        using var docker = TestcontainersSettings.OS.DockerEndpointAuthConfig
                                                 .GetDockerClientBuilder(Guid.Empty)
                                                 .Build();

        var containers = await docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["id"] = new Dictionary<string, bool> { [id] = true }
            }
        });

        return containers.Count > 0;
    }

    private sealed class DelegateDependency(
        Action? onStart = null,
        Action? onStop = null,
        Action<IComponent>? onStopComponent = null) : IComponentDependency
    {
        public Task OnStartAsync(IComponent component, CancellationToken cancellationToken)
        {
            onStart?.Invoke();
            return Task.CompletedTask;
        }

        public Task OnStopAsync(IComponent component, CancellationToken cancellationToken)
        {
            onStop?.Invoke();
            onStopComponent?.Invoke(component);
            return Task.CompletedTask;
        }
    }

    private sealed class ShutdownProbe(Action onStop) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            onStop();
            return Task.CompletedTask;
        }
    }

    private sealed class FlakyRedisHarness : ContainerHarness<IContainer>
    {
        public override string? GetConnectionString() => null;

        protected override IContainer OnCreateContainer() =>
            FailFirstDisposeProxy.Wrap(new RedisBuilder("redis:7.0").Build());
    }

    public class FailFirstDisposeProxy : DispatchProxy
    {
        private IContainer _inner = null!;
        private int _disposeAttempts;

        public static IContainer Wrap(IContainer inner)
        {
            var proxy = Create<IContainer, FailFirstDisposeProxy>();
            ((FailFirstDisposeProxy)(object)proxy)._inner = inner;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);

            if (targetMethod.Name == nameof(IAsyncDisposable.DisposeAsync) &&
                Interlocked.Increment(ref _disposeAttempts) == 1)
            {
                return ValueTask.FromException(new IOException("Simulated removal failure."));
            }

            try
            {
                return targetMethod.Invoke(_inner, args);
            }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            {
                throw e.InnerException;
            }
        }
    }
}
