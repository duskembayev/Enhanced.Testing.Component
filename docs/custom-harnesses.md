# Writing a custom harness

A harness is anything the application under test depends on: a container, a fake, a client. If there is no package
for your dependency, write a harness in a few lines.

## A container harness

Derive from `ContainerHarness<TContainer>` to wrap any [Testcontainers module](https://testcontainers.com/modules/?language=dotnet):

```csharp
public class MongoHarness : ContainerHarness<MongoDbContainer>
{
    public override string GetConnectionString() => Container.GetConnectionString();

    protected override MongoDbContainer OnCreateContainer() =>
        new MongoDbBuilder("mongo:8.0").Build();
}
```

You get the container lifecycle for free: it starts in parallel with other containers, `ConnectionName` passes the
connection string to the application as `ConnectionStrings:{ConnectionName}`, and the container is removed when the
component stops. Override `OnStart` (call `base.OnStart` first) to prepare the container, for example to create
topics or users.

If your container needs another harness while it starts, set `StartConcurrently = false` in the harness constructor
(it is an `init` property, so users can also set it). See [Component lifecycle](lifecycle.md#ordering-harnesses).

## Any other harness

Derive from `Harness` and override what you need:

- `OnConfigure(IWebHostBuilder)` changes the application before it is built: settings, services, test doubles.
  It runs after every harness has started, so it can use their addresses.
- `OnStart(CancellationToken)` starts the dependency.
- `OnStop(CancellationToken)` stops it. Make it safe to call twice.

```csharp
public class FeatureFlagsHarness : Harness
{
    private readonly Dictionary<string, bool> _flags = new();

    public void Enable(string flag) => _flags[flag] = true;

    public override void OnConfigure(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
            services.AddSingleton<IFeatureFlags>(new InMemoryFeatureFlags(_flags)));
}
```

Inside a harness, `Component` gives access to the running application (`Services`, `CreateClient`) once it has
started; `ThrowIfComponentNotStarted` guards members that need it.

The harnesses in this repository are good examples: `HttpClientHarness` is a few lines, `WireMockHarness` runs an
in-process server, and `KafkaHarness` extends a container harness with topic creation.
