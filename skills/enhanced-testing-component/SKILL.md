---
name: enhanced-testing-component
description: Write and fix component (integration) tests for ASP.NET Core applications with the Enhanced.Testing.Component NuGet packages - hosting the app in memory with real PostgreSQL, Kafka or Redis containers (Testcontainers), EF Core, gRPC, WireMock fakes for external HTTP APIs and a fake TimeProvider. Use when a project references Enhanced.Testing.Component*, when asked to add component/integration tests for an ASP.NET Core service with databases, message brokers or caches, or when such tests are slow, flaky or fail to start.
license: MIT
compatibility: Requires the .NET SDK. PostgreSQL, Kafka, Redpanda and Redis harnesses need a running Docker daemon.
---

# Enhanced.Testing.Component

Component tests start the real application in memory (`WebApplicationFactory`) together with its dependencies, which
the library calls **harnesses**: containers (PostgreSQL, Kafka, Redis), clients (HTTP, gRPC), data helpers (EF Core,
Redis database, Kafka producer/consumer) and fakes (WireMock, time). A **component** starts them, wires their
connection strings into the app's configuration, and stops and removes everything at the end.

## Choose packages

| The app uses | Package | Harnesses |
|---|---|---|
| HTTP endpoints | `Enhanced.Testing.Component` | `HttpClientHarness` |
| gRPC services | `Enhanced.Testing.Component.GrpcClient` | `GrpcClientHarness` |
| PostgreSQL | `Enhanced.Testing.Component.PostgreSql` | `PostgreSqlHarness` |
| EF Core (any provider) | `Enhanced.Testing.Component.DbContext` | `DbContextHarness<TContext>` |
| Kafka or Redpanda | `Enhanced.Testing.Component.Kafka` | `KafkaHarness`, `RedpandaHarness`, `KafkaProducerHarness<K,V>`, `KafkaConsumerHarness<K,V>` |
| Redis or Redis Stack | `Enhanced.Testing.Component.Redis` | `RedisHarness`, `RedisStackHarness`, `RedisDatabaseHarness` |
| External HTTP APIs | `Enhanced.Testing.Component.WireMock` | `WireMockHarness` |
| `TimeProvider` (delays, timeouts, retries, timers) | `Enhanced.Testing.Component.Time` | `TimeProviderHarness` |

All packages target .NET 8–11. Container harnesses need Docker; the others don't.

## Set up a test project

1. Reference the application project and add the packages, for example
   `dotnet add package Enhanced.Testing.Component.PostgreSql`.
2. Make `Program` visible to the tests. With top-level statements before .NET 10, `Program` is internal: add
   `public partial class Program;` at the end of `Program.cs` or `<InternalsVisibleTo Include="MyApp.Tests" />` to the
   app's project file.
3. Find how the app reads each dependency's address (`ConnectionStrings:X`, `builder.Configuration["X"]`) and use that
   name in the harness (`ConnectionName = "X"` for containers, `ConfigurationKey = "X"` for WireMock). Don't add
   test-only code to the app.
4. Write one fixture per test class (or collection) and keep it running for all its tests.

```csharp
public class OrdersFixture : IAsyncLifetime
{
    private readonly IComponent _component;

    public OrdersFixture()
    {
        OrdersDb.EnsureCreatedOnStart(builder => builder.UseNpgsql(PostgreSql.GetConnectionString()));

        _component = ComponentBuilder.Create<Program>()
                                     .AddHarness(PostgreSql)
                                     .AddHarness(OrdersDb)
                                     .AddHarness(Http)
                                     .Build();
    }

    public PostgreSqlHarness PostgreSql { get; } = new() { ConnectionName = "Orders" };

    public DbContextHarness<OrdersDbContext> OrdersDb { get; } = new();

    public HttpClientHarness Http { get; } = new();

    public Task InitializeAsync() => _component.StartAsync(CancellationToken.None);

    public Task DisposeAsync() => _component.StopAsync(CancellationToken.None);
}

public class OrdersTests(OrdersFixture fixture) : IClassFixture<OrdersFixture>
{
    [Fact]
    public async Task ShouldStoreOrder()
    {
        var response = await fixture.Http.CreateClient().PostAsJsonAsync("/orders", new { Item = "book" });

        response.EnsureSuccessStatusCode();
        Assert.True(await fixture.OrdersDb.ExecuteAsync(db => db.Orders.AnyAsync(o => o.Item == "book")));
    }
}
```

This is the xUnit v2 shape; in xUnit v3 `InitializeAsync`/`DisposeAsync` return `ValueTask`. For other frameworks,
call `StartAsync`/`StopAsync` from the class or assembly setup and teardown.

More templates (Kafka, Redis, WireMock, time, gRPC): [references/templates.md](references/templates.md).

## Rules

- **Order harnesses by dependency.** Add a harness after the ones it uses (`DbContextHarness` after
  `PostgreSqlHarness`, Kafka consumer/producer after the broker, `RedisDatabaseHarness` after the Redis harness).
  Containers start first and in parallel regardless of order; a container that needs another harness during its own
  start must set `StartConcurrently = false`.
- **Share containers, isolate data.** Don't start a component per test. Give tests their own data: a PostgreSQL
  database copied from a template (`CreateTemplateAsync` once, `CreateDatabaseAsync` per test), a Kafka topic per test
  (`CreateUniqueTopicAsync`), a Redis database or key prefix per fixture (`DefaultDatabase`, `KeyPrefix`,
  `FlushAsync`).
- **Wait for your own result, never sleep.** For Kafka use `WaitForKeyAsync`/`WaitForMessageAsync` with a generous
  timeout (10 s); they skip unrelated messages and return as soon as the match arrives. Never use `Task.Delay` or read
  "the next" message.
- **Control time instead of waiting.** If the app waits on timeouts, retries or timers, inject `TimeProvider` in the
  app and use `TimeProviderHarness` + `Advance(...)` in tests.
- **Fake external HTTP APIs** with `WireMockHarness`; call `Reset()` between tests that share it.
- **Remove background work the test doesn't need** with `ComponentBuilder.Create<Program>().RemoveHostedService<T>()`.
- **Use the defaults for speed.** Containers start in parallel, Kafka uses `apache/kafka-native`, PostgreSQL and Redis
  run without durability. Only set `Durable = true` when a test needs persistence or mounts its own data volume.
- **Customize containers through the harness**, not by subclassing: `Image = "<image>:<tag>"` (each container
  harness exposes its default as `DefaultImage`), `ConfigureContainer = b => b.WithEnvironment(...)`.

## When something fails

See [references/troubleshooting.md](references/troubleshooting.md) for error messages and their fixes (Docker not
running, `Program` inaccessible, "Component not started.", Kafka timeouts, template database errors, flaky tests).

## API reference (XML docs)

Every package ships XML documentation for its public API next to the assembly, matching the exact version the project
uses. Read it instead of decompiling or reflecting over the assemblies:

```
<NuGet packages folder>/<package id in lowercase>/<version>/lib/<tfm>/<package id>.xml
```

- The packages folder is `~/.nuget/packages` (`%UserProfile%\.nuget\packages` on Windows) unless `NUGET_PACKAGES` is
  set; `dotnet nuget locals global-packages --list` prints it.
- Take `<version>` from the test project's `PackageReference` (or from `Directory.Packages.props` with central package
  management); `obj/project.assets.json` lists the resolved version under `libraries` as `"<package id>/<version>"`.
  Take the `tfm` folder closest to the test project's target framework (`net8.0`, `net9.0`, `net10.0`, `net11.0`).
- Members are keyed by documentation IDs: `T:` types, `M:` methods, `P:` properties, for example
  `T:Enhanced.Testing.Component.Kafka.KafkaHarness` or
  ``M:Enhanced.Testing.Component.Kafka.KafkaConsumerHarness`2.Seek(Confluent.Kafka.Offset,System.TimeSpan)``
  (`` `2 `` marks a generic type with two type parameters). Search the file for the type name to see all its members:

```shell
grep -A6 'Enhanced.Testing.Component.Kafka.KafkaHarnessExtensions' \
  ~/.nuget/packages/enhanced.testing.component.kafka/<version>/lib/<tfm>/Enhanced.Testing.Component.Kafka.xml
```

On Windows without `grep`, use `Select-String -Context 0,6 -Pattern '<type name>' -Path <file>` in PowerShell.

If the project references the library's source instead of the packages, read the `///` comments in `src/` directly.

## Documentation

Each package README is the authoritative reference for its API and options:
[core](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component/README.md),
[PostgreSql](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.PostgreSql/README.md),
[DbContext](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.DbContext/README.md),
[Kafka](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.Kafka/README.md),
[Redis](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.Redis/README.md),
[GrpcClient](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.GrpcClient/README.md),
[WireMock](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.WireMock/README.md),
[Time](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.Time/README.md).
Guides: [lifecycle](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/docs/lifecycle.md),
[test isolation](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/docs/test-isolation.md),
[fast tests](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/docs/fast-tests.md),
[custom harnesses](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/docs/custom-harnesses.md).
