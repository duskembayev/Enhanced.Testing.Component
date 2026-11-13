# Containers

[← Back to README](../README.md)

PostgreSQL, Kafka and Redis run as real containers via [Testcontainers](https://dotnet.testcontainers.org/). Set
`ConnectionName` on a container harness and its connection string is passed to the application under test as
`ConnectionStrings:{ConnectionName}`, so the app needs no test-specific configuration.

## Image and builder

Container harnesses let you override the image and customize the underlying Testcontainers builder:

```csharp
var postgreSql = new PostgreSqlHarness
{
    Image = "postgres:17",
    ConfigureContainer = builder => builder
        .WithEnvironment("TZ", "UTC")
        .WithResourceMapping("init.sql", "/docker-entrypoint-initdb.d/")
};
```

`ConfigureContainer` runs after the harness has applied its own settings, so it can override them (except the
`RedisHarness` persistence arguments described below).

For Kafka brokers, see [Kafka](kafka.md).

## Fast, non-durable databases

Test data does not need to survive a crash, so container harnesses trade durability for speed by default:

- `PostgreSqlHarness` keeps the data directory (`PGDATA`) on tmpfs and runs with `fsync`, `full_page_writes` and
  `synchronous_commit` off. Nothing is written to disk, which reduces IO load when many containers run in parallel.
- `RedisHarness` and `RedisStackHarness` disable persistence (no RDB snapshots, no append-only file).

Set `Durable = true` to get the image's durable behaviour instead, for example when a test restarts the server and
expects the data to survive, or when the database is too large to keep in memory:

```csharp
var postgreSql = new PostgreSqlHarness { Durable = true };
```

`PostgreSqlHarness` mounts tmpfs at `/var/lib/postgresql/data`; if `ConfigureContainer` mounts a volume or bind mount
there, Docker rejects the duplicate mount point, so set `Durable = true` in that case.

`RedisHarness` appends `--save "" --appendonly no` to the container command after `ConfigureContainer` has run, so a
custom command (`redis-server /usr/local/etc/redis/redis.conf` or extra `--option value` pairs) keeps working, but its
`save` and `appendonly` settings are overridden unless `Durable = true`. `RedisStackHarness` sets the `REDIS_ARGS`
environment variable instead; setting `REDIS_ARGS` yourself replaces the harness value.
