# Enhanced.Testing.Component.Redis

[![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component.Redis)](https://www.nuget.org/packages/Enhanced.Testing.Component.Redis)

Redis harnesses for [Enhanced.Testing.Component](https://github.com/duskembayev/Enhanced.Testing.Component): a Redis or
Redis Stack container, direct access to its data from the test, and a database per fixture.

```shell
dotnet add package Enhanced.Testing.Component.Redis
```

## Usage

```csharp
var redis = new RedisStackHarness { ConnectionName = "Redis" };
var redisDatabase = new RedisDatabaseHarness(redis);

var component = ComponentBuilder.Create<Program>()
                                .AddHarness(redis)
                                .AddHarness(redisDatabase)
                                .AddHarness(http)
                                .Build();
```

The application reads `ConnectionStrings:Redis`. In a test, read or write the same data through
`RedisDatabaseHarness`:

```csharp
var value = redisDatabase.Database.StringGet("visits:Leo");
```

- `RedisHarness` runs `redis` (`redis:7.0`); `RedisStackHarness` runs `redis/redis-stack-server` with the Redis Stack
  modules (JSON, search and others).
- `RedisDatabaseHarness` exposes `Database` (`IDatabase`) and `Multiplexer`. Its connection allows admin commands.

## Isolating data

- `DatabaseNumber` selects the database the harness uses, and `KeyPrefix` scopes its keys.
- `FlushAsync` deletes the harness's data: keys with `KeyPrefix` when set, otherwise the whole database.
- To keep fixtures that share one Redis server apart, give each application its own database. `DefaultDatabase` on
  `RedisHarness`/`RedisStackHarness` adds `defaultDatabase=N` to the connection string passed to the application, and
  `RedisDatabaseHarness` follows it:

```csharp
var redis = new RedisHarness { ConnectionName = "Redis", DefaultDatabase = 3 };
var redisDatabase = new RedisDatabaseHarness(redis);

await redisDatabase.FlushAsync();
```

Redis has 16 databases (0–15) unless its `databases` setting is changed.

## Fast, non-durable by default

Test data does not need to survive a restart, so both harnesses disable persistence (no RDB snapshots, no append-only
file). Set `Durable = true` to keep the image's persistence settings.

`RedisHarness` appends `--save "" --appendonly no` to the container command after `ConfigureContainer` has run, so a
custom command (`redis-server /usr/local/etc/redis/redis.conf` or extra `--option value` pairs) keeps working, but its
`save` and `appendonly` settings are overridden unless `Durable = true`. `RedisStackHarness` sets the `REDIS_ARGS`
environment variable instead; setting `REDIS_ARGS` yourself replaces the harness value.

`ConnectionName`, `Image`, `ConfigureContainer` and `StartConcurrently` are shared by all container harnesses; see
[Enhanced.Testing.Component](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component/README.md#container-harnesses).
