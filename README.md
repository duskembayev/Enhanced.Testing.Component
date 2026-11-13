<h1 align="center">Enhanced.Testing.Component</h1>

<p align="center">
  Component tests for ASP.NET Core with real dependencies: PostgreSQL, Kafka, Redis and more, in a few lines of code.
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/Enhanced.Testing.Component"><img src="https://img.shields.io/nuget/v/Enhanced.Testing.Component" alt="NuGet" /></a>
  <a href="https://github.com/duskembayev/Enhanced.Testing.Component/actions/workflows/build.yml"><img src="https://github.com/duskembayev/Enhanced.Testing.Component/actions/workflows/build.yml/badge.svg?branch=main" alt="Build" /></a>
  <a href="https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/LICENSE"><img src="https://img.shields.io/github/license/duskembayev/Enhanced.Testing.Component" alt="License" /></a>
  <img src="https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010%20%7C%2011-512BD4" alt=".NET 8 | 9 | 10 | 11" />
</p>

## Why

- **Real dependencies.** PostgreSQL, Kafka and Redis run in Docker via [Testcontainers](https://dotnet.testcontainers.org/),
  and their connection strings reach your application through its normal configuration. No test-only code in the app.
- **One lifecycle.** A component starts the containers, then your application in memory, and stops and removes
  everything at the end, including after a failed start.
- **Fast.** Containers start in parallel, databases run without durability, Kafka uses the native image, and
  PostgreSQL template databases give each test its own copy in milliseconds.
- **Deterministic.** Wait for *your* Kafka message instead of the next one, isolate data per test, and move time
  forward instead of sleeping.

## Quick start

```shell
dotnet add package Enhanced.Testing.Component.PostgreSql
```

```csharp
public class OrdersFixture : IAsyncLifetime
{
    private readonly IComponent _component;

    public OrdersFixture()
    {
        _component = ComponentBuilder.Create<Program>()
                                     .AddHarness(PostgreSql)
                                     .AddHarness(Http)
                                     .Build();
    }

    public PostgreSqlHarness PostgreSql { get; } = new() { ConnectionName = "Orders" };

    public HttpClientHarness Http { get; } = new();

    public Task InitializeAsync() => _component.StartAsync(CancellationToken.None);

    public Task DisposeAsync() => _component.StopAsync(CancellationToken.None);
}

public class OrdersTests(OrdersFixture fixture) : IClassFixture<OrdersFixture>
{
    [Fact]
    public async Task ShouldCreateOrder()
    {
        var response = await fixture.Http.CreateClient().PostAsJsonAsync("/orders", new { Item = "book" });

        response.EnsureSuccessStatusCode();
    }
}
```

The test project references the application project. `Program` must be visible to it: with top-level statements before
.NET 10, add `<InternalsVisibleTo Include="MyApp.Tests" />` to the application project or declare
`public partial class Program;`. The example uses xUnit v2; in xUnit v3, `InitializeAsync` and `DisposeAsync` return
`ValueTask`.

The application reads `ConnectionStrings:Orders` as usual and gets the container's connection string. See
[`SampleServiceFixture`](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/examples/SampleService.Tests/SampleServiceFixture.cs)
for a fixture with PostgreSQL, Kafka, Redis and gRPC.

## Packages

| Package | What it gives you | NuGet |
|---|---|---|
| [`Enhanced.Testing.Component`](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component/README.md) | The component, `HttpClientHarness`, base classes for custom harnesses | [![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component)](https://www.nuget.org/packages/Enhanced.Testing.Component) |
| [`Enhanced.Testing.Component.PostgreSql`](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.PostgreSql/README.md) | PostgreSQL container, per-test template databases | [![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component.PostgreSql)](https://www.nuget.org/packages/Enhanced.Testing.Component.PostgreSql) |
| [`Enhanced.Testing.Component.DbContext`](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.DbContext/README.md) | EF Core: create the schema on start, query the application's database | [![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component.DbContext)](https://www.nuget.org/packages/Enhanced.Testing.Component.DbContext) |
| [`Enhanced.Testing.Component.Kafka`](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.Kafka/README.md) | Kafka (native, Confluent) or Redpanda, producer and consumer harnesses, per-test topics | [![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component.Kafka)](https://www.nuget.org/packages/Enhanced.Testing.Component.Kafka) |
| [`Enhanced.Testing.Component.Redis`](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.Redis/README.md) | Redis or Redis Stack, direct database access, per-test databases | [![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component.Redis)](https://www.nuget.org/packages/Enhanced.Testing.Component.Redis) |
| [`Enhanced.Testing.Component.GrpcClient`](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.GrpcClient/README.md) | gRPC clients that call the application in memory | [![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component.GrpcClient)](https://www.nuget.org/packages/Enhanced.Testing.Component.GrpcClient) |
| [`Enhanced.Testing.Component.WireMock`](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.WireMock/README.md) | In-process fakes for external HTTP APIs | [![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component.WireMock)](https://www.nuget.org/packages/Enhanced.Testing.Component.WireMock) |
| [`Enhanced.Testing.Component.Time`](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.Time/README.md) | A fake `TimeProvider` the test controls | [![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component.Time)](https://www.nuget.org/packages/Enhanced.Testing.Component.Time) |

## Features

Each package has its own README with examples, which is also shown on nuget.org:

- **[Lifecycle, customization, custom harnesses](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component/README.md)**: parallel container
  start, rollback on failure, removing hosted services, your own `WebApplicationFactory`, image and builder options for
  every container.
- **[Kafka](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.Kafka/README.md)**: native, Confluent or Redpanda brokers, waiting for a
  specific message, a topic per test.
- **[PostgreSQL](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.PostgreSql/README.md)** and
  **[EF Core](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.DbContext/README.md)**: schema on start, queries against the
  application's database, template databases, non-durable defaults.
- **[Redis](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.Redis/README.md)**: direct database access, a database per fixture,
  flushing test data.
- **[External HTTP APIs](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.WireMock/README.md)** and
  **[time](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.Time/README.md)**: WireMock stubs and a fake `TimeProvider` for timeouts,
  retries and timers.
- **[gRPC](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.GrpcClient/README.md)**: generated clients that call the application in
  memory.

## Guides

- [Component lifecycle](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/docs/lifecycle.md): what happens on start and stop, and how to order harnesses.
- [Test isolation](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/docs/test-isolation.md): sharing containers between tests while keeping their data apart.
- [Fast component tests](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/docs/fast-tests.md): what makes component tests slow and how to speed them up.
- [Writing a custom harness](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/docs/custom-harnesses.md): plugging in a dependency the library doesn't cover.

## Using with AI coding agents

The repository includes an [Agent Skill](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/skills/enhanced-testing-component/SKILL.md)
that teaches coding agents how to write component tests with these packages: choosing packages, fixture templates,
rules for fast and deterministic tests, and troubleshooting. It uses the open [Agent Skills](https://agentskills.io)
format, supported by Claude Code, Codex, GitHub Copilot, Cursor, Gemini CLI and others. Install it with the
[`skills`](https://github.com/vercel-labs/skills) CLI (requires Node.js), which asks which agents to install it for:

```shell
npx skills add duskembayev/Enhanced.Testing.Component
```

Add `-g` to install it for all your projects, or `-a <agent>` (for example `-a codex`) to skip the agent prompt. To
install it by hand, copy the `skills/enhanced-testing-component` folder into your agent's skills folder, for example
`.agents/skills/` or `.claude/skills/` in your repository.

Agents without skill support can read [`llms.txt`](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/llms.txt),
an index of the documentation. Every package also ships XML documentation, which the skill tells agents how to find.

## Requirements

- .NET 8, 9, 10 or 11.
- Docker for the container harnesses (PostgreSQL, Kafka, Redis). The HTTP, gRPC, WireMock and time harnesses don't
  need it.
- Any test framework: the component only needs to be started and stopped, for example from an xUnit fixture.

## Releases

Packages are published to nuget.org by CI when a `vX.Y.Z` tag is pushed. Versions follow
[semantic versioning](https://semver.org/), and package validation guards against breaking API changes within a major
version. See [Releases](https://github.com/duskembayev/Enhanced.Testing.Component/releases) for the changelog.

## License

[MIT](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/LICENSE)
