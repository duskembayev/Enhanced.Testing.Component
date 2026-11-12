using Enhanced.Testing.Component;
using Enhanced.Testing.Component.Redis;
using Microsoft.Extensions.DependencyInjection;
using SampleService.Services;
using StackExchange.Redis;

namespace SampleService.Tests;

public sealed class RedisIsolationFixture : IAsyncLifetime
{
    public const int ApplicationDatabase = 3;

    private readonly IComponent _component;

    public RedisIsolationFixture()
    {
        Redis = new RedisHarness
        {
            DefaultDatabase = ApplicationDatabase, ConnectionName = GreeterService.RedisConnectionStringName
        };
        ApplicationDatabaseHarness = new RedisDatabaseHarness(Redis);
        First = new RedisDatabaseHarness(Redis) { DatabaseNumber = 1 };
        Second = new RedisDatabaseHarness(Redis) { DatabaseNumber = 2 };
        Prefixed = new RedisDatabaseHarness(Redis) { DatabaseNumber = 4, KeyPrefix = "t[1]*:" };
        Unprefixed = new RedisDatabaseHarness(Redis) { DatabaseNumber = 4 };

        _component = ComponentBuilder.Create<Program>()
                                     .AddHarness(Redis)
                                     .AddHarness(ApplicationDatabaseHarness)
                                     .AddHarness(First)
                                     .AddHarness(Second)
                                     .AddHarness(Prefixed)
                                     .AddHarness(Unprefixed)
                                     .Build();
    }

    public RedisHarness Redis { get; }

    public RedisDatabaseHarness ApplicationDatabaseHarness { get; }

    public RedisDatabaseHarness First { get; }

    public RedisDatabaseHarness Second { get; }

    public RedisDatabaseHarness Prefixed { get; }

    public RedisDatabaseHarness Unprefixed { get; }

    public IServiceProvider Services => _component.Services;

    Task IAsyncLifetime.InitializeAsync() => _component.StartAsync(default);

    Task IAsyncLifetime.DisposeAsync() => _component.StopAsync(default);
}

public class RedisIsolationTests(RedisIsolationFixture fixture) : IClassFixture<RedisIsolationFixture>
{
    [Fact]
    public async Task ShouldPassDefaultDatabaseToApplication()
    {
        var multiplexer = fixture.Services.GetRequiredService<IConnectionMultiplexer>();
        var key = Guid.NewGuid().ToString("N");

        await multiplexer.GetDatabase().StringSetAsync(key, "app");

        Assert.Equal(RedisIsolationFixture.ApplicationDatabase, multiplexer.GetDatabase().Database);
        Assert.Equal(RedisIsolationFixture.ApplicationDatabase, fixture.ApplicationDatabaseHarness.Database.Database);
        Assert.Equal("app", await fixture.ApplicationDatabaseHarness.Database.StringGetAsync(key));
        Assert.False(await fixture.First.Database.KeyExistsAsync(key));
    }

    [Fact]
    public async Task ShouldIsolateDatabasesAndFlushOnlyOwnDatabase()
    {
        const string key = "shared-key";

        await Task.WhenAll(fixture.First.Database.StringSetAsync(key, "first"),
            fixture.Second.Database.StringSetAsync(key, "second"));

        Assert.Equal("first", await fixture.First.Database.StringGetAsync(key));
        Assert.Equal("second", await fixture.Second.Database.StringGetAsync(key));

        await fixture.First.FlushAsync();

        Assert.False(await fixture.First.Database.KeyExistsAsync(key));
        Assert.Equal("second", await fixture.Second.Database.StringGetAsync(key));
    }

    [Fact]
    public async Task ShouldFlushOnlyKeysWithPrefix()
    {
        var keys = Enumerable.Range(0, 2500).Select(i => (RedisKey)$"key-{i}").ToArray();

        foreach (var chunk in keys.Chunk(500))
        {
            await fixture.Prefixed.Database.StringSetAsync(chunk.Select(k => KeyValuePair.Create(k, (RedisValue)"v"))
                                                               .ToArray());
        }

        await fixture.Unprefixed.Database.StringSetAsync("t1x:other", "kept");
        await fixture.Unprefixed.Database.StringSetAsync("unrelated", "kept");

        await fixture.Prefixed.FlushAsync();

        Assert.Equal(0, await fixture.Prefixed.Database.KeyExistsAsync(keys));
        Assert.Equal("kept", await fixture.Unprefixed.Database.StringGetAsync("t1x:other"));
        Assert.Equal("kept", await fixture.Unprefixed.Database.StringGetAsync("unrelated"));
    }

    [Fact]
    public void ShouldIncludeDefaultDatabaseInConnectionString()
    {
        Assert.EndsWith(",defaultDatabase=3", fixture.Redis.GetConnectionString());
    }
}
