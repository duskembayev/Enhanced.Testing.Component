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
    private const string TestRunLabel = "enhanced.testing.component.test-run";

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

    [Fact]
    public async Task ShouldStartContainerHarnessesConcurrently()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new FakeContainerHarness(async ct =>
        {
            firstEntered.SetResult();
            await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
        });
        var second = new FakeContainerHarness(async ct =>
        {
            secondEntered.SetResult();
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
        });
        bool? containersStartedBeforeDependent = null;
        var component = ComponentBuilder.Create<Program>()
                                        .AddDependency(new DelegateDependency(onStart: () =>
                                            containersStartedBeforeDependent = first.Started && second.Started))
                                        .AddHarness(first)
                                        .AddHarness(second)
                                        .Build();

        try
        {
            await component.StartAsync(default);
        }
        finally
        {
            await component.StopAsync(default);
        }

        Assert.True(containersStartedBeforeDependent);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
    }

    [Fact]
    public async Task ShouldStartEnvironmentContainerHarnessesConcurrently()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new FakeContainerHarness(async ct =>
        {
            firstEntered.SetResult();
            await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
        });
        var second = new FakeContainerHarness(async ct =>
        {
            secondEntered.SetResult();
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
        });
        bool? containersStartedBeforeSequential = null;

        var environment = await ComponentEnvironment.Create()
                                                    .AddHarness(new DelegateEnvironmentHarness(onStart: () =>
                                                        containersStartedBeforeSequential =
                                                            first.Started && second.Started))
                                                    .AddHarness(first)
                                                    .AddHarness(second)
                                                    .StartAsync();

        await environment.DisposeAsync();

        Assert.True(containersStartedBeforeSequential);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
        Assert.False(first.IsStarted);
    }

    [Fact]
    public async Task ShouldStopDependenciesInReverseRegistrationOrder()
    {
        var events = new List<string>();
        var component = ComponentBuilder.Create<Program>()
                                        .AddDependency(new DelegateDependency(onStop: () => events.Add("first")))
                                        .AddHarness(new FakeContainerHarness(_ => Task.CompletedTask,
                                            () => events.Add("container")))
                                        .AddDependency(new DelegateDependency(onStop: () => events.Add("last")))
                                        .Build();

        await component.StartAsync(default);
        await component.StopAsync(default);

        Assert.Equal(["last", "container", "first"], events);
    }

    [Fact]
    public async Task ShouldWaitForParallelStartsAndRollBackWhenOneContainerFails()
    {
        var failure = new InvalidOperationException("Container failed to start.");
        var slowEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowCompleted = false;
        var slowCompletedBeforeDispose = false;
        var dependentStarted = false;
        var slow = new FakeContainerHarness(async ct =>
            {
                slowEntered.SetResult();
                await failed.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
                await Task.Delay(200, ct);
                slowCompleted = true;
            },
            () => slowCompletedBeforeDispose = slowCompleted);
        var failing = new FakeContainerHarness(async ct =>
        {
            await slowEntered.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            failed.SetResult();
            throw failure;
        });
        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(slow)
                                        .AddHarness(failing)
                                        .AddDependency(new DelegateDependency(onStart: () => dependentStarted = true))
                                        .Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => component.StartAsync(default));

        Assert.Same(failure, exception);
        Assert.True(slowCompletedBeforeDispose);
        Assert.Equal(1, slow.DisposeCount);
        Assert.Equal(1, failing.DisposeCount);
        Assert.False(dependentStarted);
        Assert.Throws<InvalidOperationException>(() => slow.Container);

        await component.StopAsync(default);
    }

    [Fact]
    public async Task ShouldAggregateFailuresOfParallelContainerStarts()
    {
        var first = new InvalidOperationException("First failed.");
        var second = new InvalidOperationException("Second failed.");
        var healthy = new FakeContainerHarness(_ => Task.CompletedTask);
        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(new FakeContainerHarness(_ => throw first))
                                        .AddHarness(healthy)
                                        .AddHarness(new FakeContainerHarness(_ => throw second))
                                        .Build();

        var exception = await Assert.ThrowsAsync<AggregateException>(() => component.StartAsync(default));

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Contains(first, exception.InnerExceptions);
        Assert.Contains(second, exception.InnerExceptions);
        Assert.Equal(1, healthy.DisposeCount);
    }

    [Fact]
    public async Task ShouldSurfaceCancellationOfParallelContainerStarts()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var first = new FakeContainerHarness(ct => Task.FromCanceled(ct));
        var second = new FakeContainerHarness(ct => Task.FromCanceled(ct));
        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(first)
                                        .AddHarness(second)
                                        .Build();

        var exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => component.StartAsync(cts.Token));

        Assert.Equal(cts.Token, exception.CancellationToken);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
    }

    [Fact]
    public async Task ShouldReportContainerStartCancellationNotRequestedByCallerAsFailure()
    {
        var timeout = new TaskCanceledException("Container start timed out.");
        var failure = new InvalidOperationException("Container failed to start.");
        var healthy = new FakeContainerHarness(_ => Task.CompletedTask);
        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(new FakeContainerHarness(_ => throw timeout))
                                        .AddHarness(healthy)
                                        .AddHarness(new FakeContainerHarness(_ => throw failure))
                                        .Build();

        var exception = await Assert.ThrowsAsync<AggregateException>(() => component.StartAsync(default));

        Assert.Equal([timeout, failure], exception.InnerExceptions);
        Assert.Equal(1, healthy.DisposeCount);
    }

    [Fact]
    public async Task ShouldRethrowSingleContainerStartCancellationNotRequestedByCaller()
    {
        var timeout = new TaskCanceledException("Container start timed out.");
        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(new FakeContainerHarness(_ => Task.CompletedTask))
                                        .AddHarness(new FakeContainerHarness(_ => throw timeout))
                                        .Build();

        var exception = await Assert.ThrowsAsync<TaskCanceledException>(() => component.StartAsync(default));

        Assert.Same(timeout, exception);
    }

    [Fact]
    public async Task ShouldPreferFailureOverCallerCancellationOfParallelStarts()
    {
        using var cts = new CancellationTokenSource();
        var failure = new InvalidOperationException("Container failed to start.");
        var waitingEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = new FakeContainerHarness(async ct =>
        {
            waitingEntered.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });
        var failing = new FakeContainerHarness(async _ =>
        {
            await waitingEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await cts.CancelAsync();
            throw failure;
        });
        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(waiting)
                                        .AddHarness(failing)
                                        .Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => component.StartAsync(cts.Token));

        Assert.Same(failure, exception);
        Assert.Equal(1, waiting.DisposeCount);
        Assert.Equal(1, failing.DisposeCount);
    }

    [Fact]
    public async Task ShouldNotStartContainerHarnessRegisteredTwiceConcurrently()
    {
        var starts = 0;
        var harness = new FakeContainerHarness(_ =>
        {
            Interlocked.Increment(ref starts);
            return Task.CompletedTask;
        });
        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(harness)
                                        .AddHarness(harness)
                                        .Build();

        await Assert.ThrowsAsync<NotSupportedException>(() => component.StartAsync(default));

        Assert.Equal(1, starts);
        Assert.Equal(1, harness.DisposeCount);
    }

    [Fact]
    public async Task ShouldStartContainerHarnessInRegistrationOrderWhenNotConcurrent()
    {
        var events = new List<string>();
        var first = new FakeContainerHarness(_ =>
        {
            events.Add("container");
            return Task.CompletedTask;
        });
        var dependent = new FakeContainerHarness(_ =>
        {
            events.Add(first.Started ? "dependent container" : "dependent container before container");
            return Task.CompletedTask;
        })
        {
            StartConcurrently = false
        };
        var component = ComponentBuilder.Create<Program>()
                                        .AddDependency(new DelegateDependency(onStart: () => events.Add("dependency")))
                                        .AddHarness(dependent)
                                        .AddHarness(first)
                                        .Build();

        try
        {
            await component.StartAsync(default);
        }
        finally
        {
            await component.StopAsync(default);
        }

        Assert.Equal(["container", "dependency", "dependent container"], events);
        Assert.Equal(1, dependent.DisposeCount);
    }

    [Fact]
    public async Task ShouldRemoveRealContainersWhenParallelStartFails()
    {
        var label = Guid.NewGuid().ToString("N");
        var redis = new StartTrackingRedisHarness
        {
            ConfigureContainer = builder => builder.WithLabel(TestRunLabel, label)
        };
        var failure = new InvalidOperationException("Container failed to start.");
        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(redis)
                                        .AddHarness(new FakeContainerHarness(_ => throw failure))
                                        .Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => component.StartAsync(default));

        Assert.Same(failure, exception);
        Assert.True(redis.Started);
        Assert.False(await ContainerWithLabelExistsAsync(label));
        Assert.Throws<InvalidOperationException>(() => redis.Container);
    }

    [Fact]
    public async Task ShouldStopComponentsAndRemoveContainersWhenEnvironmentIsDisposed()
    {
        var redis = new RedisHarness();
        var httpClient = new HttpClientHarness();
        var environment = await ComponentEnvironment.Create().AddHarness(redis).StartAsync();

        string containerId;
        IComponent component;

        try
        {
            containerId = redis.Container.Id;
            component = await environment.CreateComponent<Program>().AddHarness(httpClient).StartAsync();
            Assert.Equal(TestcontainersStates.Running, redis.Container.State);
        }
        finally
        {
            await environment.DisposeAsync();
        }

        Assert.False(await ContainerExistsAsync(containerId));
        Assert.Throws<InvalidOperationException>(() => component.Services);
        Assert.Throws<InvalidOperationException>(() => httpClient.CreateClient());
        Assert.Throws<InvalidOperationException>(() => redis.Container);
        Assert.Throws<InvalidOperationException>(() => environment.CreateComponent<Program>());

        await environment.DisposeAsync();
    }

    [Fact]
    public async Task ShouldRemoveContainersWhenEnvironmentHarnessFailsToStop()
    {
        var first = new RedisHarness();
        var second = new RedisHarness();
        var failure = new InvalidOperationException("Stop failed.");
        var environment = await ComponentEnvironment.Create()
                                                    .AddHarness(first)
                                                    .AddHarness(new DelegateEnvironmentHarness(onStop: () => throw failure))
                                                    .AddHarness(second)
                                                    .StartAsync();

        string[] containerIds = [first.Container.Id, second.Container.Id];

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => environment.StopAsync());
        Assert.Same(failure, exception);

        foreach (var id in containerIds)
        {
            Assert.False(await ContainerExistsAsync(id));
        }
    }

    [Fact]
    public async Task ShouldRemoveContainersWhenEnvironmentStartFails()
    {
        var redis = new RedisHarness();
        string? containerId = null;
        var builder = ComponentEnvironment.Create()
                                          .AddHarness(redis)
                                          .AddHarness(new DelegateEnvironmentHarness(onStart: () =>
                                          {
                                              containerId = redis.Container.Id;
                                              throw new InvalidOperationException("Start failed.");
                                          }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.StartAsync());

        Assert.NotNull(containerId);
        Assert.False(await ContainerExistsAsync(containerId));
    }

    [Fact]
    public async Task ShouldSurfaceCancelledEnvironmentStopAndKeepCleanupRetryable()
    {
        var redis = new RedisHarness();
        var environment = await ComponentEnvironment.Create().AddHarness(redis).StartAsync();
        var containerId = redis.Container.Id;

        try
        {
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(() => environment.StopAsync(cts.Token));
            Assert.True(await ContainerExistsAsync(containerId));
        }
        finally
        {
            await environment.DisposeAsync();
        }

        Assert.False(await ContainerExistsAsync(containerId));
    }

    private static Task<bool> ContainerExistsAsync(string id) => ContainerMatchesAsync("id", id);

    private static Task<bool> ContainerWithLabelExistsAsync(string label) =>
        ContainerMatchesAsync("label", $"{TestRunLabel}={label}");

    private static async Task<bool> ContainerMatchesAsync(string filter, string value)
    {
        using var docker = TestcontainersSettings.OS.DockerEndpointAuthConfig
                                                 .GetDockerClientBuilder(Guid.Empty)
                                                 .Build();

        var containers = await docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                [filter] = new Dictionary<string, bool> { [value] = true }
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

    private sealed class DelegateEnvironmentHarness(Action? onStart = null, Action? onStop = null)
        : IEnvironmentHarness
    {
        public void OnConfigure(IWebHostBuilder builder)
        {
        }

        public Task OnStartAsync(IComponentEnvironment environment, CancellationToken cancellationToken)
        {
            onStart?.Invoke();
            return Task.CompletedTask;
        }

        public Task OnStopAsync(IComponentEnvironment environment, CancellationToken cancellationToken)
        {
            onStop?.Invoke();
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

    private sealed class StartTrackingRedisHarness : RedisHarness
    {
        public bool Started { get; private set; }

        protected override async Task OnStart(CancellationToken cancellationToken)
        {
            await base.OnStart(cancellationToken);
            Started = true;
        }
    }

    private sealed class FakeContainerHarness(Func<CancellationToken, Task> onStart, Action? onDispose = null)
        : ContainerHarness<IContainer>
    {
        private int _disposeCount;
        private volatile bool _started;

        public bool Started => _started;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public override string? GetConnectionString() => null;

        protected override IContainer OnCreateContainer() => FakeContainerProxy.Create(
            async ct =>
            {
                await onStart(ct);
                _started = true;
            },
            () =>
            {
                onDispose?.Invoke();
                Interlocked.Increment(ref _disposeCount);
            });
    }

    public class FakeContainerProxy : DispatchProxy
    {
        private Func<CancellationToken, Task> _onStart = null!;
        private Action _onDispose = null!;

        public static IContainer Create(Func<CancellationToken, Task> onStart, Action onDispose)
        {
            var proxy = Create<IContainer, FakeContainerProxy>();
            var fake = (FakeContainerProxy)(object)proxy;
            fake._onStart = onStart;
            fake._onDispose = onDispose;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);

            switch (targetMethod.Name)
            {
                case nameof(IContainer.StartAsync):
                    return _onStart((CancellationToken)args![0]!);
                case nameof(IAsyncDisposable.DisposeAsync):
                    _onDispose();
                    return ValueTask.CompletedTask;
            }

            if (targetMethod.ReturnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            return targetMethod.ReturnType.IsValueType ? Activator.CreateInstance(targetMethod.ReturnType) : null;
        }
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
