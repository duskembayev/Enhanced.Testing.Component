using Enhanced.Testing.Component;
using Enhanced.Testing.Component.PostgreSql;
using Enhanced.Testing.Component.Redis;
using Testcontainers.Redis;

namespace SampleService.Tests;

public class ContainerConfigurationTests
{
    [Fact]
    public async Task ShouldUseConfiguredImageAndBuilder()
    {
        var redis = new RedisHarness
        {
            Image = "redis:7.4",
            ConfigureContainer = builder => builder.WithEnvironment("SAMPLE_VARIABLE", "configured")
        };

        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(redis)
                                        .Build();

        await component.StartAsync(default);

        try
        {
            Assert.Equal("redis:7.4", redis.Container.Image.FullName);

            var result = await redis.Container.ExecAsync(["printenv", "SAMPLE_VARIABLE"]);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("configured", result.Stdout.Trim());
        }
        finally
        {
            await component.StopAsync(default);
        }
    }

    [Theory]
    [InlineData(false, "off|off|off")]
    [InlineData(true, "on|on|on")]
    public async Task ShouldApplyPostgreSqlDurability(bool durable, string expectedSettings)
    {
        var postgreSql = new PostgreSqlHarness { Durable = durable };

        await RunAsync(postgreSql, async () =>
        {
            var mount = await postgreSql.Container.ExecAsync(
                ["sh", "-c", "awk '$2 == \"/var/lib/postgresql/data\" { print $3 }' /proc/mounts"]);

            var settings = await postgreSql.Container.ExecAsync(
                ["psql", "-U", "postgres", "-tAc", "SELECT current_setting('fsync') || '|' || current_setting('full_page_writes') || '|' || current_setting('synchronous_commit')"]);

            Assert.Equal(!durable, mount.Stdout.Trim() == "tmpfs");
            Assert.Equal(expectedSettings, settings.Stdout.Trim());
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ShouldApplyRedisDurability(bool stack, bool durable)
    {
        ContainerHarness<RedisContainer> redis = stack
            ? new RedisStackHarness { Durable = durable }
            : new RedisHarness { Durable = durable };

        await RunAsync(redis, async () =>
        {
            var save = await redis.Container.ExecAsync(["redis-cli", "--raw", "config", "get", "save"]);
            var appendOnly = await redis.Container.ExecAsync(["redis-cli", "--raw", "config", "get", "appendonly"]);

            Assert.Equal(durable ? "save\n3600 1 300 100 60 10000" : "save", save.Stdout.Trim());
            Assert.Equal("appendonly\nno", appendOnly.Stdout.Trim());
        });
    }

    [Fact]
    public async Task ShouldApplyConfigureContainerAfterDurabilityDefaults()
    {
        var redis = new RedisStackHarness
        {
            ConfigureContainer = builder => builder.WithEnvironment("REDIS_ARGS", "--save 60 1")
        };

        await RunAsync(redis, async () =>
        {
            var save = await redis.Container.ExecAsync(["redis-cli", "--raw", "config", "get", "save"]);

            Assert.Equal("save\n60 1", save.Stdout.Trim());
        });
    }

    [Fact]
    public async Task ShouldDisableRedisPersistenceAfterCustomServerCommand()
    {
        var redis = new RedisHarness
        {
            ConfigureContainer = builder => builder.WithCommand("redis-server", "--save", "60 1", "--maxmemory", "10mb")
        };

        await RunAsync(redis, async () =>
        {
            var save = await redis.Container.ExecAsync(["redis-cli", "--raw", "config", "get", "save"]);
            var maxMemory = await redis.Container.ExecAsync(["redis-cli", "--raw", "config", "get", "maxmemory"]);

            Assert.Equal("save", save.Stdout.Trim());
            Assert.Equal("maxmemory\n10485760", maxMemory.Stdout.Trim());
        });
    }

    private static async Task RunAsync(IHarness harness, Func<Task> assert)
    {
        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(harness)
                                        .Build();

        await component.StartAsync(default);

        try
        {
            await assert();
        }
        finally
        {
            await component.StopAsync(default);
        }
    }
}
