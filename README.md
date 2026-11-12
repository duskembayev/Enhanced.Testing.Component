# Enhanced.Testing.Component

[![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component)](https://www.nuget.org/packages/Enhanced.Testing.Component)

This solution provides a set of harnesses for integrating various external components into your .NET testing
environment. It simplifies the process of setting up and tearing down external dependencies such as databases, message
brokers, and other services during testing.

## Harnesses

The solution includes harnesses for the following components:

- **PostgreSQL**: Facilitates the setup of a PostgreSQL database container for integration testing.
  [![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component.PostgreSql)](https://www.nuget.org/packages/Enhanced.Testing.Component.PostgreSql)
- **Redis**: Provides support for both Redis and RedisStack, allowing for easy integration of Redis services into your
  tests.
  [![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component.Redis)](https://www.nuget.org/packages/Enhanced.Testing.Component.Redis)
- **Kafka**: Harnesses for a Kafka broker (Confluent, Apache Kafka native or Redpanda) for message-based integration
  testing.
  [![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component.Kafka)](https://www.nuget.org/packages/Enhanced.Testing.Component.Kafka)
- **gRPC Client**: Simplifies the process of testing gRPC services by providing a client harness.
  [![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component.GrpcClient)](https://www.nuget.org/packages/Enhanced.Testing.Component.GrpcClient)
- **HTTP Client**: Offers a harness for testing HTTP-based services and APIs.
  [![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component)](https://www.nuget.org/packages/Enhanced.Testing.Component)

## Getting Started

To use these testing components in your project, follow these steps:

1. **Add Dependencies**: Ensure your project references the `Enhanced.Testing.Component` namespace and its dependencies.

2. **Configure Test Fixtures**: Use the provided harnesses in your test fixtures for setting up and tearing down
   external services. See the `SampleServiceFixture` class for an example.

3. **Integration Testing**: Write your integration tests using the configured fixtures to interact with real instances
   of your external dependencies.

## Example Usage

Below is an example of how to set up a test fixture using the PostgreSQL harness:

```csharp
public class MyServiceFixture : IAsyncLifetime
{
    private readonly IComponent _component;

    public MyServiceFixture()
    {
        HttpClient = new HttpClientHarness();
        PostgreSql = new PostgreSqlHarness { ConnectionName = "PeopleDb" };
        PeopleDb = new DbContextHarness<PeopleDbContext>();
        PeopleDb.EnsureCreatedOnStart(builder => builder.UseNpgsql(PostgreSql.GetConnectionString()));

        _component = ComponentBuilder.Create<Program>()
                                     .AddHarness(HttpClient)
                                     .AddHarness(PostgreSql)
                                     .AddHarness(PeopleDb)
                                     .Build();
    }

    public HttpClientHarness HttpClient { get; }

    public PostgreSqlHarness PostgreSql { get; }

    public DbContextHarness<PeopleDbContext> PeopleDb { get; }

    public Task InitializeAsync() => _component.StartAsync(CancellationToken.None);

    public Task DisposeAsync() => _component.StopAsync(CancellationToken.None);
}
```

## Features

### Real dependencies in Docker

PostgreSQL, Kafka and Redis run as real containers via [Testcontainers](https://dotnet.testcontainers.org/). Set
`ConnectionName` on a container harness and its connection string is passed to the application under test as
`ConnectionStrings:{ConnectionName}`, so the app needs no test-specific configuration.

### Managed lifecycle

`StartAsync` starts the harnesses in the order they were added, then the application. `StopAsync` stops the
application first, then the harnesses in reverse order, and removes their containers (except
[reused](#reusing-containers-between-local-runs) ones). Add a harness after the
harnesses it depends on (for example, `DbContextHarness` after `PostgreSqlHarness`).

### Clients for the application under test

`HttpClientHarness` and `GrpcClientHarness` create clients that call the application in memory through `TestServer`,
without opening network ports.

### Test data and assertions

- `DbContextHarness<TContext>` creates the schema on start (`EnsureCreatedOnStart`) and runs queries against the
  application's database (`ExecuteAsync`).
- `KafkaHarness` and `RedpandaHarness` create topics on start (`Topics`; existing topics are kept) and wait until they
  have a partition leader. `KafkaProducerHarness` sends messages to the application, and `KafkaConsumerHarness` reads
  what it produced. The consumer is positioned at the end of the topic on start; prefer waiting for *your* message
  over reading the next one, so messages left by other tests or a slow producer don't make the test flaky:

  ```csharp
  var message = await consumer.WaitForKeyAsync("order-42", TimeSpan.FromSeconds(10));
  var result = await consumer.WaitForMessageAsync(r => r.Message.Value.Contains("42"), TimeSpan.FromSeconds(10));
  ```

  Non-matching messages are skipped; a `TimeoutException` reports how many. `SeekToBeginning`/`SeekToEnd` move the
  position the wait starts from.
- `RedisDatabaseHarness` gives direct access to the Redis database (`Database`).

### Per-test PostgreSQL databases from a template

To isolate tests that write to PostgreSQL without starting a container per test, prepare a template database once and
copy it for each test. `CREATE DATABASE ... TEMPLATE` copies the database at the storage level, so a copy stays fast
(about 60 ms, mostly `docker exec` overhead) even when building the schema and seed data takes much longer:

```csharp
await postgreSql.CreateTemplateAsync("people_template", async connectionString =>
{
    var options = new DbContextOptionsBuilder<PeopleDbContext>().UseNpgsql(connectionString).Options;
    await using var context = new PeopleDbContext(options);
    await context.Database.EnsureCreatedAsync();
});

var connectionString = await postgreSql.CreateDatabaseAsync($"people_{Guid.NewGuid():N}", "people_template");
```

- `CreateTemplateAsync` creates the database (dropping an existing one with that name), runs your callback with a
  non-pooled connection string, then marks the database as a template, blocks new connections to it and closes the
  open ones. PostgreSQL refuses to copy a template that has open connections.
- `CreateDatabaseAsync` returns the new database's connection string. Without a template it creates an empty database.
- `DropDatabaseAsync` drops a database (or a template) and closes its connections. `GetConnectionString(database)`
  returns the connection string for any database in the container.

The SQL runs through `psql` inside the container, so the package doesn't depend on Npgsql. Requires PostgreSQL 13 or
later.

### Configuring containers

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

Any Kafka-compatible broker works with the producer and consumer harnesses (they accept `IKafkaHarness`).
`KafkaHarness` uses `apache/kafka-native` (a GraalVM build of Apache Kafka) by default: it starts fastest and has the
smallest image. Use `KafkaHarness.ConfluentImage` for the Confluent Platform image, or `RedpandaHarness` for
applications that run Redpanda in production:

```csharp
var kafka = new KafkaHarness { Topics = ["orders"] };
var confluent = new KafkaHarness { Image = KafkaHarness.ConfluentImage, Topics = ["orders"] };
var redpanda = new RedpandaHarness { Topics = ["orders"], ConnectionName = "Kafka" };
var consumer = new KafkaConsumerHarness<string, string>(redpanda) { Topic = "orders" };
```

### Fast, non-durable databases

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

### Reusing containers between local runs

For a faster edit-and-run loop on your machine, set `Reuse = true` on `PostgreSqlHarness`, `RedisHarness` or
`RedisStackHarness`. The harness then uses Testcontainers' reuse feature: the first run creates the container, `StopAsync`
leaves it running, and later runs attach to it instead of starting a new one. On a warm run PostgreSQL is ready in
under 100 ms instead of 1–2 s.

```csharp
var postgreSql = new PostgreSqlHarness
{
    Reuse = Environment.GetEnvironmentVariable("CI") is null
};
```

Use it for local runs only, never in CI:

- Reused containers are not removed by Testcontainers' resource reaper (Ryuk). They keep running until you remove
  them: `docker rm -f $(docker ps -aq --filter label=org.testcontainers.reuse-hash)`.
- Data survives between runs. `EnsureCreated` doesn't update an existing schema, and tests see rows written by
  earlier runs. Write tests that don't depend on an empty database, or remove the container after a schema change.
- A container is matched by a hash of its configuration (image, environment, command, labels and so on). Harnesses with
  the same configuration share one container, including test classes that run in parallel and test runs for several
  target frameworks (starts are serialized through a lock file in the temporary directory, so parallel processes
  don't create duplicates). Such harnesses also share their data. If Testcontainers still finds two matching containers
  ("Sequence contains more than one element"), remove them with the command above. Give a harness its own container by
  adding a label:
  `ConfigureContainer = builder => builder.WithLabel("reuse-scope", "orders-tests")`.
- Changing the configuration creates a new container. The old one keeps running until you remove it.

### Custom harnesses

Derive from `Harness` (override `OnConfigure`, `OnStart`, `OnStop`) to plug in any dependency, or from
`ContainerHarness<TContainer>` (override `OnCreateContainer`, `GetConnectionString`) for any Testcontainers module. To
support `Reuse`, call `WithReuse(Reuse)` on the builder in `OnCreateContainer` and override `SupportsReuse` to return
`true`.
