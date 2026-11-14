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

## Environments

A `ComponentEnvironment` owns harnesses that outlive the components created from it, usually containers.
`StartAsync` on the environment builder starts the container harnesses in parallel, then the others in order; if one
fails, those already started are stopped again. Each component created with `CreateComponent` then goes through the
steps above with its own harnesses only: the environment's harnesses are already running, and their configuration is
applied to the component's host before the component's own.

Stopping a component leaves the environment's harnesses running. Stopping (or disposing) the environment stops the
components created from it that are still running, then the environment's harnesses in reverse order. As with a
component, a failing step does not prevent the others, and calling `StopAsync` again retries what did not complete.

## Sharing a component between tests

Starting containers is the slowest part of a test run, so start a component once per test class or collection (for
example, in an xUnit class fixture) and keep tests independent through their data. See
[Test isolation](test-isolation.md). When tests need a freshly started application, share an environment instead and
create a component per test; only the application host is started again.
