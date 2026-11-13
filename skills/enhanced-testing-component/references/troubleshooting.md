# Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `DockerUnavailableException` or "Docker is either not running or misconfigured" | Docker isn't running or isn't reachable | Start Docker. Only container harnesses need it. |
| `CS0122: 'Program' is inaccessible due to its protection level` | Top-level `Program` is internal (before .NET 10) | Add `public partial class Program;` to `Program.cs` or `InternalsVisibleTo` for the test project. |
| `CS0436: The type '...' conflicts with the imported type` for gRPC messages | The `.proto` is generated in both the service and the test project, which references the service | Generate client and server in the service (`GrpcServices="Both"`) and remove the `Protobuf` item from the tests. |
| `InvalidOperationException: Component not started.` (`The harness is not started.` from 2.0) | A harness was used before the component started it, after the component stopped it, or it was never added with `AddHarness` | Add the harness with `AddHarness`, and use it only between the fixture's start and dispose. |
| `InvalidOperationException: The component has not been started.` | A harness called the component (`CreateClient`, `Services`) from its own start, before the app started | Call the app from tests, not from a harness's start; an HTTP or gRPC client harness can only be used after `StartAsync` returns. |
| `InvalidOperationException: The component has been stopped.` | The component was used after `StopAsync`, or a harness touched it while stopping | Don't use the component or its harnesses after the fixture is disposed. |
| `InvalidOperationException: The component can be started only once.` | `StartAsync` was called twice | Build a new component instead of restarting one. |
| `NotSupportedException: The harness is already started by a component.` (`... by another component or environment.` from 2.0) | One harness instance was added to two running components, or an environment's harness was added to a component | Create a separate harness instance per component; don't add the environment's harnesses to its components. |
| `InvalidOperationException: The component has already been built.` (2.0) | `Build()` or `StartAsync()` was called twice on one builder | Create a new builder (`ComponentBuilder.Create` or `Environment.CreateComponent`) per component. |
| `InvalidOperationException: The environment is not started.` (2.0) | A component was created from an environment that was not started or is already stopped | Create components between the environment fixture's start and dispose. |
| `InvalidOperationException: The harness is started by an environment and is not bound to a component.` (2.0) | A harness that needs the application (HTTP, gRPC, `DbContextHarness`) was added to the environment | Add such harnesses to each component; the environment holds containers. |
| A harness can't connect to its container during start | It was added before the harness it depends on | Reorder: add it after its dependency. A dependent *container* needs `StartConcurrently = false`. |
| The app can't find its connection string / uses `localhost` defaults | `ConnectionName`/`ConfigurationKey` doesn't match the key the app reads | Use the exact configuration key (`ConnectionStrings:{ConnectionName}` for containers). |
| `InvalidOperationException: Failed to create Kafka topic '...'` | Invalid topic name or broker error (inner `KafkaException`) | Use letters, digits, `.`, `_`, `-` only. |
| `TimeoutException` from `WaitForKeyAsync`/`WaitForMessageAsync` | The message never arrived or went to another topic; before 2.0, also when it landed in a partition other than 0 | Check the topic name the app uses; before 2.0, make sure it produces to a single-partition topic. The message reports how many messages were skipped. |
| Kafka test passes locally, flaky on CI | Reading "the next" message or a fixed short timeout | Wait for your key with a 10 s timeout; isolate topics per test. |
| `InvalidOperationException: PostgreSQL command failed: ...` | Template/database SQL failed (name in use, template has connections, PostgreSQL < 13) | Use unique names (≤ 63 bytes), let `CreateTemplateAsync` close connections, use PostgreSQL 13+. |
| "Duplicate mount point" when starting PostgreSQL | A volume is mounted at the data directory while the harness uses tmpfs | Set `Durable = true` on the harness. |
| A Redis config file's `save`/`appendonly` are ignored | The harness disables persistence by default | Set `Durable = true`. |
| Time-based test hangs | The app doesn't use `TimeProvider` from DI, or the clock was advanced before the code started waiting | Inject `TimeProvider`; advance after the request started waiting, or keep advancing until it completes. |
| Tests slow down or time out when several target frameworks run | Every target framework starts its own containers in parallel | Set `<TestTfmsInParallel>false</TestTfmsInParallel>` in the test project. |
| Containers left running after a crashed run | The test process was killed | Testcontainers' Ryuk removes them; otherwise `docker ps -a --filter label=org.testcontainers=true`. |
