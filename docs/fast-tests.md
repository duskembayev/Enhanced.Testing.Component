# Fast component tests

Most of the time in component tests goes into starting containers and waiting for asynchronous work. This guide lists
what the library does by default and what you can do on top.

## What you get by default

- **Containers start in parallel.** Startup time is that of the slowest container, not the sum of all of them. See
  [Component lifecycle](lifecycle.md).
- **Light images.** Kafka uses `apache/kafka-native`, a GraalVM build that starts faster than the JVM image and is
  much smaller.
- **No durability.** PostgreSQL keeps its data on tmpfs, and Redis doesn't write snapshots. Test data doesn't need to
  survive a crash; set `Durable = true` on a harness when it does.

## What you can do

1. **Measure first.** Run tests with `dotnet test --logger trx` and look at where the time goes: container startup,
   image pulls on CI, application startup, or waiting inside tests.
2. **Start containers once.** Share a component per test class or collection instead of per test, and isolate tests
   through their data. See [Test isolation](test-isolation.md).
3. **Copy databases instead of creating them.** Build the schema and seed data once in a PostgreSQL template database
   and copy it for each test in milliseconds.
4. **Never sleep.** Replace `Task.Delay` and fixed timeouts with waiting for a condition. For Kafka, wait for your
   message with `WaitForKeyAsync`/`WaitForMessageAsync`.
5. **Control time.** If the application waits for timeouts, retries or timers, inject `TimeProvider` and replace it in
   tests with a fake clock (`TimeProviderHarness`), then move the clock instead of waiting.
6. **Fake what you don't need for real.** External HTTP APIs can run in-process with `WireMockHarness`, without
   containers or network calls. Remove hosted services the test doesn't need with `RemoveHostedService<T>()`.
7. **Make CI cheap.** Pin image versions and cache images between runs. When a test project targets several
   frameworks, run the full suite on one and a smaller set on the others, or run them sequentially if the runner gets
   overloaded.
