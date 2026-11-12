using Docker.DotNet.Models;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Enhanced.Testing.Component;
using Enhanced.Testing.Component.PostgreSql;
using Enhanced.Testing.Component.Redis;
using Testcontainers.Redis;

namespace SampleService.Tests;

public sealed class ContainerReuseTests : IAsyncLifetime
{
    private const string RunLabel = "enhanced.testing.component.tests.reuse-run";

    private readonly string _runId = Guid.NewGuid().ToString("N");

    [Fact]
    public async Task ShouldKeepReusableRedisContainerRunningAndReuseIt()
    {
        var first = new RedisHarness { Reuse = true, ConfigureContainer = b => b.WithLabel(RunLabel, _runId) };
        var second = new RedisHarness { Reuse = true, ConfigureContainer = b => b.WithLabel(RunLabel, _runId) };

        var firstId = await RunAsync(first, async () =>
        {
            var result = await first.Container.ExecAsync(["redis-cli", "set", "reused", "yes"]);
            Assert.Equal(0, result.ExitCode);
        });

        Assert.True(await IsRunningAsync(firstId));

        var secondId = await RunAsync(second, async () =>
        {
            var result = await second.Container.ExecAsync(["redis-cli", "--raw", "get", "reused"]);
            Assert.Equal("yes", result.Stdout.Trim());
        });

        Assert.Equal(firstId, secondId);
        Assert.True(await IsRunningAsync(secondId));
    }

    [Fact]
    public async Task ShouldReusePostgreSqlContainerWithItsData()
    {
        var first = new PostgreSqlHarness { Reuse = true, ConfigureContainer = b => b.WithLabel(RunLabel, _runId) };
        var second = new PostgreSqlHarness { Reuse = true, ConfigureContainer = b => b.WithLabel(RunLabel, _runId) };

        var firstId = await RunAsync(first, async () =>
        {
            var result = await first.Container.ExecScriptAsync("CREATE TABLE reused (id int);");
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Stderr);
        });

        var secondId = await RunAsync(second, async () =>
        {
            var result = await second.Container.ExecScriptAsync("SELECT count(*) FROM reused;");
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Stderr);
        });

        Assert.Equal(firstId, secondId);
        Assert.True(await IsRunningAsync(secondId));
    }

    [Fact]
    public async Task ShouldRemoveContainerWhenHarnessDoesNotSupportReuse()
    {
        var harness = new CustomRedisHarness { Reuse = true };

        var id = await RunAsync(harness, () => Task.CompletedTask);

        Assert.False(await ExistsAsync(id));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var docker = CreateDockerClient();

        var containers = await docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool> { [RunLabel + "=" + _runId] = true }
            }
        });

        foreach (var container in containers)
        {
            await docker.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters { Force = true });
        }
    }

    private static async Task<string> RunAsync<TContainer>(ContainerHarness<TContainer> harness, Func<Task> action)
        where TContainer : IContainer
    {
        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(harness)
                                        .Build();

        string id;

        try
        {
            await component.StartAsync(default);
            id = harness.Container.Id;
            await action();
        }
        finally
        {
            await component.StopAsync(default);
        }

        return id;
    }

    private static async Task<bool> IsRunningAsync(string id)
    {
        using var docker = CreateDockerClient();
        var response = await docker.Containers.InspectContainerAsync(id);
        return response.State?.Running == true;
    }

    private static async Task<bool> ExistsAsync(string id)
    {
        using var docker = CreateDockerClient();

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

    private static Docker.DotNet.DockerClient CreateDockerClient() =>
        TestcontainersSettings.OS.DockerEndpointAuthConfig.GetDockerClientBuilder(Guid.Empty).Build();

    private sealed class CustomRedisHarness : ContainerHarness<RedisContainer>
    {
        public override string? GetConnectionString() => null;

        protected override RedisContainer OnCreateContainer() => new RedisBuilder("redis:7.0").Build();
    }
}
