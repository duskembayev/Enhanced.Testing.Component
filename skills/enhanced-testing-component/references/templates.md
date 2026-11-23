# Templates

Fixtures below use xUnit v2 (`IAsyncLifetime` returning `Task`) and C# 12. Adapt names to the application: the
`ConnectionName`/`ConfigurationKey` values must match what the application reads from configuration.

Every template shares its fixture through one collection definition per test project, so the containers and the
application start once for all test classes:

```csharp
[CollectionDefinition("Orders")]
public class OrdersCollection : ICollectionFixture<OrdersFixture>;
```

Namespaces: `Enhanced.Testing.Component` plus the package's own (`Enhanced.Testing.Component.Kafka`,
`.PostgreSql`, `.DbContext`, `.Redis`, `.WireMock`, `.Time`, `.GrpcClient`), `System.Net.Http.Json` for
`PostAsJsonAsync`, `Microsoft.EntityFrameworkCore` for EF Core queries, `WireMock.RequestBuilders` and
`WireMock.ResponseBuilders` for WireMock stubs, and `Grpc.Core` for `StatusCode`.

## Kafka: the app produces, the test checks

```csharp
public class OrdersFixture : IAsyncLifetime
{
    private readonly IComponent _component;

    public OrdersFixture()
    {
        Consumer = new KafkaConsumerHarness<string, string>(Kafka) { Topic = "orders" };

        _component = ComponentBuilder.Create<Program>()
                                     .AddHarness(Kafka)
                                     .AddHarness(Consumer)
                                     .AddHarness(Http)
                                     .Build();
    }

    public KafkaHarness Kafka { get; } = new() { Topics = ["orders"], ConnectionName = "Kafka" };

    public KafkaConsumerHarness<string, string> Consumer { get; }

    public HttpClientHarness Http { get; } = new();

    public Task InitializeAsync() => _component.StartAsync(CancellationToken.None);

    public Task DisposeAsync() => _component.StopAsync(CancellationToken.None);
}

[Collection("Orders")]
public class OrdersTests(OrdersFixture fixture)
{
    [Fact]
    public async Task ShouldPublishOrderCreated()
    {
        await fixture.Http.CreateClient().PostAsJsonAsync("/orders", new { Id = 42 });

        var message = await fixture.Consumer.WaitForKeyAsync("42", TimeSpan.FromSeconds(10));

        Assert.Contains("created", message.Value);
    }
}
```

## Kafka: the test produces, the app consumes

Add a `KafkaProducerHarness<string, string>` (after the broker) and send to the topic the app listens to, then assert
on the app's observable effect (database row, HTTP response) by polling with a deadline, not by sleeping:

```csharp
await fixture.Producer.ProduceAsync("order-42", "{\"id\":42}");

await WaitUntilAsync(() => fixture.OrdersDb.ExecuteAsync(db => db.Orders.AnyAsync(o => o.Id == 42)),
                     TimeSpan.FromSeconds(10));

static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;

    while (!await condition())
    {
        if (DateTime.UtcNow > deadline)
        {
            throw new TimeoutException("The condition was not met in time.");
        }

        await Task.Delay(100);
    }
}
```

## Kafka: a topic per test

Use when tests share a broker and must not see each other's messages (the app is not involved):

```csharp
var topic = await fixture.Kafka.CreateUniqueTopicAsync("orders");
using var consumer = await fixture.Kafka.CreateConsumerAsync<string, string>(topic);
using var producer = await fixture.Kafka.CreateProducerAsync<string, string>(topic);

await producer.ProduceAsync("order-42", "created");
var message = await consumer.WaitForKeyAsync("order-42", TimeSpan.FromSeconds(10));
```

Use `RedpandaHarness` instead of `KafkaHarness` if the app runs Redpanda in production, or
`Image = KafkaHarness.ConfluentImage` for the Confluent image. The consumer harnesses read partition 0 only.

## PostgreSQL: a database per test

```csharp
public async Task InitializeAsync()
{
    await _component.StartAsync(CancellationToken.None);

    await PostgreSql.CreateTemplateAsync("orders_template", async connectionString =>
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>().UseNpgsql(connectionString).Options;
        await using var context = new OrdersDbContext(options);
        await context.Database.EnsureCreatedAsync();
    });
}
```

```csharp
var connectionString = await fixture.PostgreSql.CreateDatabaseAsync($"orders_{Guid.NewGuid():N}", "orders_template");
```

The copy is for the test's own direct database access; the app keeps the database it was configured with at start.

## Redis

```csharp
public RedisStackHarness Redis { get; } = new() { ConnectionName = "Redis", DefaultDatabase = 1 };
```

Add `new RedisDatabaseHarness(Redis)` after it to read what the app wrote (`Database.StringGet(...)`) and to clean up
between tests (`await redisDatabase.FlushAsync()`).

## External HTTP API

```csharp
public WireMockHarness PaymentsApi { get; } = new() { ConfigurationKey = "PaymentsApi:BaseUrl" };
```

```csharp
fixture.PaymentsApi.Reset();
fixture.PaymentsApi.Server
       .Given(Request.Create().WithPath("/payments").UsingPost())
       .RespondWith(Response.Create().WithStatusCode(201));

await fixture.Http.CreateClient().PostAsJsonAsync("/orders/42/pay", new { Amount = 10 });

Assert.Single(fixture.PaymentsApi.Server.LogEntries);
```

If the app finds the API through service discovery (`Microsoft.Extensions.ServiceDiscovery`), name the service
instead of the configuration key. The app's client must call `http://payments` or `https+http://payments`; the fake
speaks plain HTTP, so `https://payments` does not reach it.

```csharp
public WireMockHarness PaymentsApi { get; } = new() { ServiceName = "payments" };
```

For a named endpoint, such as `http://_api.payments`, also set `EndpointName = "api"`.

## External gRPC service

Pass the descriptors of the services from the generated code. The harness listens over HTTP/2 and does not answer
HTTP/1.1 requests, so keep it apart from the harness for HTTP APIs.

```csharp
public WireMockGrpcHarness Payments { get; } = new(PaymentService.Descriptor) { ServiceName = "payments" };
```

Point the application's gRPC client at `http://payments`, or pass the address through `ConfigurationKey`.

```csharp
var orderId = Guid.NewGuid().ToString();

fixture.Payments.On<ChargeRequest>(request => request.OrderId == orderId)
       .Respond(new ChargeReply { Status = "charged" });

await fixture.Http.CreateClient().PostAsJsonAsync($"/orders/{orderId}/pay", new { Amount = 10 });

Assert.Single(fixture.Payments.On<ChargeRequest>(request => request.OrderId == orderId).Requests());
```

- `On<TRequest>()` without a predicate selects every call of the method. A stub with a predicate answers before one
  without, so the fixture can set defaults and a test can stub by an id of its own.
- `Fail(StatusCode.Unavailable, "Try later")` instead of `Respond` fails the call; error details of a rich status
  go after the message: `Fail(code, message, new ErrorInfo { Reason = "..." })`.
- `Respond` and `Fail` return the stub; dispose it to remove a stub that overrides a default for one test.
- If several methods take the same request type, name the method or the service: `On<ChargeRequest>("Refund")`,
  `On<ChargeRequest>(PaymentService.Descriptor)`.
- When the application calls the service later (after it consumed a message), wait for the call:
  `await fixture.Payments.On<ChargeRequest>(r => r.OrderId == orderId).WaitForRequestAsync(TimeSpan.FromSeconds(10))`.
- A call that no stub matches fails in the app with `Unimplemented`, and the status message shows the request and
  tells whether the method has stubs.
- Unary methods only. For delays, scenarios and trailers, stub through `Payments.Server` with WireMock's builders:
  `Request.Create().UsingGrpc(PaymentService.Descriptor, nameof(PaymentService.PaymentServiceClient.ChargeAsync))`
  and `Response.Create().WithGrpcMessage(reply)`, `WithGrpcStatus(...)`, `WithGrpcTrailer(key, value)`.

## Time

The app must get `TimeProvider` from DI (`builder.Services.AddSingleton(TimeProvider.System);`).

```csharp
public TimeProviderHarness Time { get; } = new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
```

```csharp
await client.PostAsJsonAsync("/subscriptions", new { Plan = "trial" });

fixture.Time.Advance(TimeSpan.FromDays(15));

var subscription = await client.GetFromJsonAsync<Subscription>("/subscriptions/current");
Assert.True(subscription!.Expired);
```

## gRPC

```csharp
public GrpcClientHarness Grpc { get; } = new();
```

```csharp
var client = fixture.Grpc.CreateClient<Greeter.GreeterClient>();
var reply = await client.SayHelloAsync(new HelloRequest { Name = "John" });
```

When the test project references the service project, generate the client in the service project
(`<Protobuf Include="Protos\greet.proto" GrpcServices="Both" />`) and don't generate the `.proto` again in the tests;
duplicate generation causes `CS0436` conflicts. Only if the tests don't reference the service, generate the client in
the test project (`GrpcServices="Client"`, with `Grpc.AspNetCore` or `Grpc.Tools` plus `Google.Protobuf`).
