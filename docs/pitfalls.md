# Common pitfalls

Problems that show up in real test suites, with their causes and fixes. Most of them appear once many test classes
share one component, and stay hidden while every class starts its own.

## A fixture for every test class

**Symptom:** the test project takes minutes, and the tests themselves take seconds.

`IClassFixture<T>` starts the fixture again for each test class, with new containers and a new application. The same
happens when `IClassFixture<T>` is put on a collection definition. Share one fixture through `ICollectionFixture<T>`
(or an assembly fixture in xUnit v3). See
[Sharing a component between tests](lifecycle.md#sharing-a-component-between-tests).

## The application misses the first Kafka messages

**Symptom:** a test that produces a message right after the component started fails now and then; adding a delay
after the start "fixes" it.

Kafka consumers start from the end of the topic by default (`auto.offset.reset=latest`). A message produced before
the application's consumer has its partitions assigned is before that position, so the application never sees it.
Do not wait after the start. Let the application's consumers read from the beginning in tests:

```csharp
ComponentBuilder.Create<Program>()
                .Configure(builder => builder.UseSetting("Kafka:Consumer:AutoOffsetReset", "Earliest"))
```

Use the configuration key the application's consumer reads; for the Aspire Kafka client it is
`Aspire:Confluent:Kafka:Consumer:Config:AutoOffsetReset`, and a named consumer has a key of its own. The setting
matters only for a consumer group without committed offsets, which is the case in a new container. The library's
own consumers (`KafkaConsumerHarness`, `CreateConsumerAsync`) start at the end of the topic and are ready when the
component has started.

## Checking that nothing happened

**Symptom:** a test waits a few seconds and then asserts that no message arrived or no row was written. It is slow,
and on a busy machine it passes before the application had a chance to do the wrong thing.

Wait for a signal instead of a pause. Send a second message, a sentinel, after the tested one and wait until the
application has handled the sentinel. If the application handles messages in order, the tested message has been
handled by then:

```csharp
await fixture.Orders.ProduceAsync(ignoredOrderId, ignoredOrder);
await fixture.Orders.ProduceAsync(sentinelOrderId, sentinelOrder);

var ignoredOrderWasConfirmed = false;

await fixture.Confirmations.WaitForMessageAsync(
    result =>
    {
        ignoredOrderWasConfirmed |= result.Message.Key == ignoredOrderId;
        return result.Message.Key == sentinelOrderId;
    },
    TimeSpan.FromSeconds(10));

Assert.False(ignoredOrderWasConfirmed);
```

The order is guaranteed only within one partition, and only if the application handles a partition's messages one
after another. Topics created by the harness have one partition.

## A setting from `ConfigureAppConfiguration` is ignored

**Symptom:** the test sets a configuration value, and the application still uses the default.

With minimal hosting, the code in `Program.cs` that reads `builder.Configuration` while it registers services runs
before the values added through `ConfigureAppConfiguration` (for example, with `AddInMemoryCollection`) are applied.
Values read later through `IOptions<T>` arrive; values read during registration do not, and nothing reports it.
`UseSetting` is applied before `Program` runs and works in both cases:

```csharp
ComponentBuilder.Create<Program>()
                .Configure(builder => builder.UseSetting("Orders:BatchTimeout", "00:00:01"))
```

## The application stops in the middle of the run

**Symptom:** from some test on, every test fails with
`ObjectDisposedException: Cannot access a disposed object. Object name: 'IServiceProvider'`.

When a `BackgroundService` throws, .NET stops the host (`BackgroundServiceExceptionBehavior.StopHost`). The
component is still marked as started, but the application behind it is gone. The cause is in the application's log:
run the tests with `--logger "console;verbosity=detailed"` and search for `BackgroundService failed`.

A typical cause is a background service that needs something the fixture does not provide, such as a Kafka topic
that was never created (`Unknown topic or partition`). Either provide it (add every topic the application produces
to in `Topics`), or remove the service when the tests do not need it:

```csharp
ComponentBuilder.Create<Program>()
                .RemoveHostedService<OutboxPublisher>()
```

A fixture per test class hides this problem: each class gets a new application, and only the tests after the failure
in the same class are affected.

## State shared between test classes

**Symptom:** a test passes alone and fails when the whole project runs, or the other way round.

Tests that share a component share the application: its caches, its data, and every fake or mock registered in it.

- Use unique identifiers in each test instead of constants, so a row, key or message left by another test does not
  match.
- Look for your own result (`WaitForKeyAsync`, a query by identifier) instead of "the next message" or "the only
  row".
- Reset fakes that tests configure: `WireMockHarness.Reset()`, and the reset method of your mocking library for mocks
  registered in the application. Before counting calls, clear the calls recorded by earlier tests.
- Clean shared data between tests when identifiers cannot be unique: `RedisDatabaseHarness.FlushAsync`, or a
  database per test from a template.

## Many test projects at once

**Symptom:** containers fail to start or time out when the whole solution is tested, while each project passes
alone.

`dotnet test` on a solution runs several test projects at once, and each of them starts its own containers. Limit the
number of projects that run in parallel, for example `dotnet test -m:4`, and choose the number from the CPUs and the
memory available to Docker.

## A local HTTP fake that starts slowly

**Symptom:** a test with a hand-written fake HTTP server takes several seconds to start.

`HttpListener` with an IP prefix (`http://127.0.0.1:5000/`) resolves the host name of the machine, which takes
seconds where the name does not resolve. Use a `localhost` prefix, or `WireMockHarness`, which does not have the
problem.
