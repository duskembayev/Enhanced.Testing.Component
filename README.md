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
- **Kafka**: A harness for setting up a Kafka broker for message-based integration testing.
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
    public HttpClientHarness HttpClient { get; private set; }
    public PostgreSqlHarness PostgreSql { get; private set; }
    public DbContextHarness<PeopleDbContext> PeopleDb { get; private set; }

    private IComponent _component;

    public MyServiceFixture()
    {
        HttpClient = new HttpClientHarness();
        PostgreSql = new PostgreSqlHarness()
        {
            ConnectionName = "PeopleDb"
        };
        PeopleDb = new DbContextHarness<PeopleDbContext>
        {
            EnsureCreated = true
        };

        _component = ComponentBuilder.Create<Program>()
                                     .AddHarness(HttpClient)
                                     .AddHarness(PostgreSql)
                                     .AddHarness(PeopleDb)
                                     .Build();
    }

    public async Task InitializeAsync()
    {
        await _component.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _component.StopAsync();
    }
}
```
