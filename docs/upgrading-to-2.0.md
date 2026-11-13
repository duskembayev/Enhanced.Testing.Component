# Upgrading to 2.0

Version 2.0 adds environments that share containers between application instances, and makes a few breaking changes
that were not possible within 1.x. Most test projects compile without changes; this guide lists what may need
attention.

## New

- **`ComponentEnvironment`** starts containers once and creates components from them, each with its own application
  host. Use it when tests need a freshly started application. See
  [A fresh application per test](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component/README.md#a-fresh-application-per-test).
- **`PostgreSqlDatabaseHarness`** gives a component a database of its own in a shared PostgreSQL container, copied
  from a template, and drops it when the component stops.
- **`IComponent` is `IAsyncDisposable`**: `await using var component = await builder.StartAsync();` builds, starts
  and stops a component. The cancellation tokens of `StartAsync` and `StopAsync` are optional.
- **`DbContextHarness.ExecuteAsync`** has overloads whose action receives the cancellation token.
- **`Harness.IsStarted`** tells whether a harness is running.

## Breaking changes

| Change | What to do |
|---|---|
| Kafka consumers (`KafkaConsumerHarness`, `CreateConsumerAsync`) read every partition of the topic instead of partition 0 | Nothing for single-partition topics. With several partitions, messages from all of them now arrive, in no particular order across partitions: wait for a specific key or predicate. |
| `KafkaConsumerHarness.Seek(Offset, TimeSpan)` became `Seek(Offset, TimeSpan? timeout = null)`, and the `Seek(Offset, int)` extension was removed | Call `Seek(offset)` or pass a `TimeSpan`. An absolute offset now applies to every partition. |
| `KafkaProducerHarness.ProduceAsync` returns `Task<DeliveryResult<TKey, TValue>>` instead of `Task` | Recompile. Code that awaits it keeps working. |
| `IComponent` extends `IAsyncDisposable` | Only custom `IComponent` implementations need a `DisposeAsync` method. |
| A component builder builds one component: a second `Build()` throws | Create a new builder for each component. |
| Harness errors read `The harness is not started.` and `The harness is already started by another component or environment.` | Update assertions that match the old messages. |
| `Harness.ThrowIfComponentNotStarted` no longer marks a field as non-null for the compiler | No action for code outside the library. |

Packages compiled against 1.x must be recompiled against 2.0.
