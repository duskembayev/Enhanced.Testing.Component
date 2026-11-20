# Component lifecycle

A component is your application plus the harnesses it depends on. This guide explains what happens when it starts
and stops, and how to order harnesses that depend on each other.

## Starting

`IComponent.StartAsync` runs in three steps:

1. **Containers, in parallel.** Every container harness (PostgreSQL, Kafka, Redis, or your own
   `ContainerHarness<TContainer>`) starts at the same time, so the startup time of several containers does not add up.
2. **Other harnesses, in order.** Harnesses that are not containers start one by one in the order they were added.
   They can use the containers, for example a `DbContextHarness` creating the schema in PostgreSQL, or a
   `KafkaConsumerHarness` connecting to the broker.
3. **The application.** The host is built last. Configuration from the harnesses, such as connection strings passed
   through `ConnectionName`, is applied at this point, so it already contains the containers' addresses.

If anything fails to start, everything started so far is stopped again before the exception is rethrown. A single
failure is rethrown as is; several failures are wrapped in an `AggregateException`.

## Stopping

`IComponent.StopAsync` stops the application first, while its dependencies are still running, so hosted services can
shut down cleanly. It then stops the harnesses in reverse order and removes their containers.

A harness that fails to stop does not prevent the others from stopping. The failure is rethrown afterwards, and
calling `StopAsync` again retries only the steps that did not complete. If the cancellation token fires, the remaining
steps are skipped and `OperationCanceledException` is thrown; calling `StopAsync` again finishes them.

## Ordering harnesses

- Add a harness after the harnesses it uses, for example `DbContextHarness` after `PostgreSqlHarness`, or a Kafka
  consumer after the broker.
- Containers always start before non-container harnesses, regardless of the order they were added.
- A container that needs another harness while it starts (for example, a Kafka Connect container that connects to a
  Kafka container) must opt out of the parallel step with `StartConcurrently = false`. It then starts in step 2, in
  the order it was added. Here `KafkaConnectHarness` stands for such a custom harness:

```csharp
var kafka = new KafkaHarness();
var connect = new KafkaConnectHarness(kafka) { StartConcurrently = false };

var component = ComponentBuilder.Create<Program>()
                                .AddHarness(kafka)
                                .AddHarness(connect)
                                .Build();
```

## Sharing a component between tests

Starting containers and the application is the slowest part of a test run, so start one component for all the test
classes that can share it and keep tests independent through their data. See [Test isolation](test-isolation.md).

In xUnit, the way a fixture is declared decides how often it starts:

| Declaration | The fixture starts |
|---|---|
| `ICollectionFixture<T>` on a collection definition | once for all test classes in the collection |
| `IClassFixture<T>` on a test class | once for that test class |
| `IClassFixture<T>` on a collection definition | once for **every** test class in the collection |
| `[assembly: AssemblyFixture(typeof(T))]` (xUnit v3) | once for the test assembly |

```csharp
[CollectionDefinition("Orders")]
public class OrdersCollection : ICollectionFixture<OrdersFixture>;

[Collection("Orders")]
public class CreateOrderTests(OrdersFixture fixture)
{
}

[Collection("Orders")]
public class CancelOrderTests(OrdersFixture fixture)
{
}
```

A class fixture suits a project with a single test class, or a class that needs an application with its own
settings. In a project with many test classes it starts new containers and a new application for each of them, which
can easily take most of the run time.

Test classes in one collection run one after another. Classes in different collections run in parallel, each with its
own fixture, so a few separate fixtures can be faster than one shared fixture when the tests themselves are slow.
Measure before choosing: in the output of `dotnet test --logger trx`, a gap of several seconds between two test
classes is a fixture starting.

Tests that share a component also share the application's state and everything registered in it. See
[Common pitfalls](pitfalls.md).
