# Test isolation

Starting a container per test is slow, and sharing one between tests makes them depend on each other's data. The
usual answer is to **share the containers and isolate the data**: each test (or fixture) gets its own database,
topic or key space inside a shared container.

## Isolate data, not containers

| Dependency | Unit of isolation | How |
|---|---|---|
| PostgreSQL | a database per test | Copy a prepared template database: `CreateTemplateAsync` once, `CreateDatabaseAsync` per test |
| Kafka | a topic per test | `CreateUniqueTopicAsync`, with `CreateConsumerAsync`/`CreateProducerAsync` bound to it |
| Redis | a database or key prefix per fixture | `DefaultDatabase` for the application, `DatabaseNumber`/`KeyPrefix` and `FlushAsync` for the test |

For example, a test that needs its own PostgreSQL database copies a template prepared once in the fixture:

```csharp
var connectionString = await fixture.PostgreSql.CreateDatabaseAsync($"orders_{Guid.NewGuid():N}", "orders_template");
```

The details are in the
[PostgreSQL](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.PostgreSql/README.md),
[Kafka](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.Kafka/README.md) and
[Redis](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component.Redis/README.md)
package READMEs.

## The application's data

Isolating the test's own data is easy; isolating what the **application** reads and writes is harder, because the
application gets its configuration when it starts. Two approaches:

- **One application per fixture.** Give each fixture's application its own database, topic names or Redis database
  through configuration (for example, `ConnectionName` plus `DefaultDatabase`, or `Configure(b => b.UseSetting(...))`).
- **Reset between tests.** Clean the data the application touched, for example `FlushAsync` for Redis or deleting
  rows through `DbContextHarness`.

## Wait for your own result

Shared dependencies mean other tests' data can show up in yours. Don't assert on "the next" message or row; look for
the one your test produced. For Kafka, wait for a specific key or predicate instead of reading the next message:

```csharp
var message = await consumer.WaitForKeyAsync("order-42", TimeSpan.FromSeconds(10));
```

Use a generous timeout: the wait returns as soon as the message arrives, so a long timeout costs nothing when the
test passes and avoids flaky failures on a slow CI runner.

## Running tests in parallel

Tests can run in parallel once their data is isolated. Keep an eye on the machine: every fixture that starts its own
containers adds load. Running several target frameworks of a test project in parallel multiplies it, which can make
containers slow to respond. Setting `TestTfmsInParallel` to `false` in the test project runs the target frameworks one
after another.
