# Enhanced.Testing.Component

[![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component)](https://www.nuget.org/packages/Enhanced.Testing.Component)

The core of [Enhanced.Testing.Component](https://github.com/duskembayev/Enhanced.Testing.Component): a component that
hosts your ASP.NET Core application in memory together with its dependencies ("harnesses"), plus an HTTP client
harness and the base classes for writing your own harnesses.

```shell
dotnet add package Enhanced.Testing.Component
```

## Usage

```csharp
public class OrdersFixture : IAsyncLifetime
{
    private readonly IComponent _component;

    public OrdersFixture()
    {
        _component = ComponentBuilder.Create<Program>()
                                     .AddHarness(Http)
                                     .Build();
    }

    public HttpClientHarness Http { get; } = new();

    public Task InitializeAsync() => _component.StartAsync();

    public Task DisposeAsync() => _component.StopAsync();
}

public class OrdersTests(OrdersFixture fixture) : IClassFixture<OrdersFixture>
{
    [Fact]
    public async Task ShouldBeHealthy()
    {
        var response = await fixture.Http.CreateClient().GetAsync("/health");

        response.EnsureSuccessStatusCode();
    }
}
```

`HttpClientHarness.CreateClient()` returns an `HttpClient` that calls the application in memory through `TestServer`,
without opening network ports.

The test project references the application project, and `Program` must be visible to it: with top-level statements
before .NET 10, add `<InternalsVisibleTo Include="MyApp.Tests" />` to the application project or declare
`public partial class Program;`. The example uses xUnit v2; in xUnit v3, `InitializeAsync` and `DisposeAsync` return
`ValueTask`.

## Lifecycle

`StartAsync` starts all container harnesses in parallel, then the other harnesses in the order they were added, then
the application; a failed start stops everything again. `StopAsync` stops the application first, then the harnesses
in reverse order, and removes the containers. Disposing a component (`await using`) stops it too. Add a harness after
the harnesses it uses. See
[Component lifecycle](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/docs/lifecycle.md) for details.

## A fresh application per test

Some applications do work at startup that a test needs to see, such as warming up a cache from the database or
reading feature flags. To start a new application for each test without starting the containers again, put the
containers in a `ComponentEnvironment` and create a component per test from it:

```csharp
public class OrdersEnvironment : IAsyncLifetime
{
    public PostgreSqlHarness PostgreSql { get; } = new() { ConnectionName = "Orders" };

    public IComponentEnvironment Environment { get; private set; } = null!;

    public async Task InitializeAsync() =>
        Environment = await ComponentEnvironment.Create()
                                                .AddHarness(PostgreSql)
                                                .StartAsync();

    public Task DisposeAsync() => Environment.StopAsync();
}

public class OrdersTests(OrdersEnvironment fixture) : IClassFixture<OrdersEnvironment>
{
    [Fact]
    public async Task ShouldServeWarmedUpCatalog()
    {
        var http = new HttpClientHarness();

        await using var component = await fixture.Environment
                                                 .CreateComponent<Program>(builder =>
                                                     builder.UseSetting("Catalog:Warmup", "true"))
                                                 .AddHarness(http)
                                                 .StartAsync();

        var response = await http.CreateClient().GetAsync("/catalog");

        response.EnsureSuccessStatusCode();
    }
}
```

- The environment starts its harnesses once (containers in parallel) and applies their configuration, such as
  connection strings, and its own `Configure` calls to every component created from it.
- `CreateComponent<TEntryPoint>()` returns a builder for a new component with its own application host. Add the
  harnesses that work with this application instance to it: HTTP and gRPC clients, `DbContextHarness`, Kafka
  producers and consumers, `RedisDatabaseHarness`, `PostgreSqlDatabaseHarness` for a database of its own. A builder
  builds one component; `StartAsync()` on the builder builds and starts it.
- Stopping or disposing a component stops its application and its own harnesses; the environment's containers keep
  running. Stopping the environment stops the components still running, then its harnesses in reverse order.
- A harness instance is used by one running component or environment at a time. It can be reused after its owner
  stops. Container harnesses can belong to either; other harnesses belong to a component.

## Container harnesses

PostgreSQL, Kafka and Redis harnesses run real containers via [Testcontainers](https://dotnet.testcontainers.org/).
They share these options:

- `ConnectionName`: the container's connection string is passed to the application as
  `ConnectionStrings:{ConnectionName}`, so the app needs no test-specific configuration.
- `Image`: the container image (each harness has a pinned `DefaultImage`).
- `ConfigureContainer`: customizes the Testcontainers builder. It runs after the harness has applied its own
  settings, so it can override them.
- `StartConcurrently`: set to `false` to start the container in registration order instead of in parallel.

```csharp
var postgreSql = new PostgreSqlHarness
{
    ConnectionName = "Orders",
    Image = "postgres:17",
    ConfigureContainer = builder => builder
        .WithEnvironment("TZ", "UTC")
        .WithResourceMapping("init.sql", "/docker-entrypoint-initdb.d/")
};
```

## Customizing the application under test

Remove background work the test does not need, or host the application through your own `WebApplicationFactory`
subclass (for example, one that overrides `CreateHost` or `ConfigureWebHost`):

```csharp
var component = ComponentBuilder.Create(new MyApplicationFactory())
                                .RemoveHostedService<OutboxPublisher>()
                                .Configure(builder => builder.UseSetting("Features:Payments", "false"))
                                .AddHarness(httpClient)
                                .Build();
```

`RemoveHostedService<TService>` runs after the application's own registrations, so it removes hosted services added
in `Program` (and by `Configure` calls made before it). It matches the exact implementation type of non-keyed
registrations; a factory registration is matched only when the factory is declared to return `TService`, as
`AddHostedService(sp => new OutboxPublisher(...))` is. The component does not dispose a factory passed to `Create`.

## Custom harnesses

Derive from `Harness` to plug in any dependency, or from `ContainerHarness<TContainer>` for any Testcontainers
module. See [Writing a custom harness](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/docs/custom-harnesses.md).

## Related packages

| Package | |
|---|---|
| [Enhanced.Testing.Component.PostgreSql](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.PostgreSql/README.md) | PostgreSQL container, per-test template databases |
| [Enhanced.Testing.Component.DbContext](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.DbContext/README.md) | EF Core schema and queries |
| [Enhanced.Testing.Component.Kafka](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.Kafka/README.md) | Kafka or Redpanda, producer and consumer |
| [Enhanced.Testing.Component.Redis](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.Redis/README.md) | Redis or Redis Stack |
| [Enhanced.Testing.Component.GrpcClient](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.GrpcClient/README.md) | gRPC clients |
| [Enhanced.Testing.Component.WireMock](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.WireMock/README.md) | Fakes for external HTTP APIs |
| [Enhanced.Testing.Component.Time](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.Time/README.md) | Fake `TimeProvider` |
