# Lifecycle and customization

[← Back to README](../README.md)

## How a component starts and stops

`StartAsync` starts all container harnesses (`ContainerHarness<TContainer>`) concurrently, so the startup time of
several containers does not add up. It then starts the other harnesses in the order they were added, and finally the
application. If anything fails to start, everything started so far is stopped again. `StopAsync` stops the application
first, then the harnesses in reverse order, and removes their containers. Add a harness after the harnesses it depends
on (for example, `DbContextHarness` after `PostgreSqlHarness`). A container harness that needs another harness while it
starts (for example, a container connecting to another container) must be created with `StartConcurrently = false`;
it is then started with the other harnesses in the order it was added.

## Clients for the application under test

`HttpClientHarness` and `GrpcClientHarness` create clients that call the application in memory through `TestServer`,
without opening network ports.

## Customizing the application under test

Remove background work the test does not need, or host the application through your own `WebApplicationFactory`
subclass (for example, one that overrides `CreateHost` or `ConfigureWebHost`):

```csharp
var component = ComponentBuilder.Create(new MyApplicationFactory())
                                .RemoveHostedService<OutboxPublisher>()
                                .AddHarness(httpClient)
                                .Build();
```

`RemoveHostedService<TService>` runs after the application's own registrations, so it removes hosted services added
in `Program` (and by `Configure` calls made before it). It matches the exact implementation type of non-keyed
registrations; a factory registration is matched only when the factory is declared to return `TService`, as
`AddHostedService(sp => new OutboxPublisher(...))` is. The component does not dispose a factory passed to `Create`.

## Custom harnesses

Derive from `Harness` (override `OnConfigure`, `OnStart`, `OnStop`) to plug in any dependency, or from
`ContainerHarness<TContainer>` (override `OnCreateContainer`, `GetConnectionString`) for any Testcontainers module.
