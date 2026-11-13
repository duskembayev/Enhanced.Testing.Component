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

    public Task InitializeAsync() => _component.StartAsync(CancellationToken.None);

    public Task DisposeAsync() => _component.StopAsync(CancellationToken.None);
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

## Lifecycle

`StartAsync` starts all container harnesses in parallel, then the other harnesses in the order they were added, then
the application; a failed start stops everything again. `StopAsync` stops the application first, then the harnesses
in reverse order, and removes the containers. Add a harness after the harnesses it uses. See
[Component lifecycle](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/docs/lifecycle.md) for details.

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
