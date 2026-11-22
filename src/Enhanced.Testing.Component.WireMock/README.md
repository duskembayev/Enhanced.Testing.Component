# Enhanced.Testing.Component.WireMock

[![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component.WireMock)](https://www.nuget.org/packages/Enhanced.Testing.Component.WireMock)

A [WireMock.Net](https://github.com/WireMock-Net/WireMock.Net) harness for
[Enhanced.Testing.Component](https://github.com/duskembayev/Enhanced.Testing.Component): fakes external HTTP APIs with an
in-process server instead of calling real services. No Docker needed.

```shell
dotnet add package Enhanced.Testing.Component.WireMock
```

## Usage

```csharp
var quotesApi = new WireMockHarness { ConfigurationKey = "QuotesApi:BaseUrl" };

var component = ComponentBuilder.Create<Program>()
                                .AddHarness(quotesApi)
                                .AddHarness(http)
                                .Build();
```

The server starts with the component on a free port, and its URL is passed to the application as the
`QuotesApi:BaseUrl` setting. Point the application's `HttpClient` at that setting.

In a test, stub the external API, call the application, and verify the calls:

```csharp
quotesApi.Server
         .Given(Request.Create().WithPath("/quote").UsingGet())
         .RespondWith(Response.Create().WithStatusCode(200).WithBody("Stay hungry"));

var response = await http.CreateClient().GetStringAsync("/quote");

Assert.Equal("Stay hungry", response);
Assert.Single(quotesApi.Server.LogEntries);
```

- `Server` is the `WireMockServer`, for stubbing and verification.
- `Reset()` removes stubs, scenarios and the request log; call it between tests that share the harness.
- `BaseAddress` and `CreateClient()` give the test the server's address or an `HttpClient` for it.
- `ConfigureServer` customizes the `WireMockServerSettings` before the server starts.

## Service discovery

An application that finds its dependencies by name with `Microsoft.Extensions.ServiceDiscovery` needs no extra
configuration key. Give the harness the service name, and it registers the server as that service:

```csharp
var payments = new WireMockHarness { ServiceName = "payments" };
var greeter = new WireMockHarness { ServiceName = "greeter", EndpointName = "grpc", UseHttp2 = true };
```

| The application calls | Harness |
|---|---|
| `http://payments` or `https+http://payments` | `ServiceName = "payments"` |
| `http://_api.payments` (a named endpoint) | `ServiceName = "payments", EndpointName = "api"` |

The address is passed to the application as `Services:{ServiceName}:{EndpointName}:0`; `EndpointName` is `http`
unless set. The server speaks plain HTTP, so a client fixed to `https://payments` cannot be pointed at it. Endpoints
of the same service listed in the application's own configuration are not removed.

## Faking a gRPC service

`WireMockGrpcHarness<TClient>` fakes a gRPC service the application calls. It takes the client that `Grpc.Tools`
generated for the service, and a stub names the call by calling that client:

```csharp
var greeter = new WireMockGrpcHarness<Greeter.GreeterClient> { ServiceName = "greeter" };
```

```csharp
greeter.Respond(client => client.SayHelloAsync(null), new HelloReply { Message = "Hello" });
greeter.Fail(client => client.SayHelloAsync(new HelloRequest { Name = "nobody" }), StatusCode.NotFound);
```

- `client => client.SayHelloAsync(null)` stands for every call of the method. With a request message instead of
  `null`, the stub answers the calls whose request equals it. The client is not connected to anything: the call only
  names the method, and the compiler checks the method and the type of the response.
- For another condition, add a predicate with the request type:
  `greeter.Respond(client => client.SayHelloAsync(null), (HelloRequest request) => request.Name.StartsWith('a'), reply)`.
- `Fail(call, statusCode, message, details)` fails the call; the details make a rich status, see
  [Error details and trailers](#error-details-and-trailers).
- `Requests<TRequest>()` returns the request messages the application sent, to assert on them:
  `Assert.Single(greeter.Requests<HelloRequest>(request => request.Name == "world"))`. Calls that no stub answered
  are included. If several methods take the same request type, the requests of all of them are returned; name the
  call to get those of one method: `greeter.Requests(client => client.SayHelloAsync(null), (HelloRequest r) => ...)`.
- `WaitForRequestAsync(call, predicate, timeout)` waits for a call the application makes later, for example after
  it consumed a message; see [Waiting for a call](#waiting-for-a-call).
- A predicate that throws does not match the call. If no other stub answers it, the status the client receives
  names the exception.
- The server listens over HTTP/2. `ServiceName`, `EndpointName` and `ConfigurationKey` work as on `WireMockHarness`;
  for a client that calls `http://_grpc.greeter`, set `EndpointName = "grpc"`.
- The harness finds the service descriptor and creates the client through reflection, so `TClient` must be a client
  generated by `Grpc.Tools`. Unary methods only.

### Which stub answers

When several stubs fit a call, a stub with a condition (a request message or a predicate) answers before a stub
without one; among equal stubs the one made last answers.

```csharp
greeter.Respond(client => client.SayHelloAsync(null), new HelloReply { Message = "Hello" });
greeter.Respond(client => client.SayHelloAsync(new HelloRequest { Name = id }), new HelloReply { Message = "Mine" });
```

`Respond` and `Fail` return the stub; dispose it to remove the stub. Stubs added through `Server` follow WireMock's
own rules and, unless they are given a priority, answer before the stubs of the harness.

### Sharing the harness between tests

When test classes share one fixture, do not call `Reset()` between tests: set the default answers in the fixture,
and give every test a scope.

```csharp
public class GreetingTests(AppFixture fixture) : IDisposable
{
    private readonly WireMockGrpcScope<Greeter.GreeterClient> _greeter = fixture.Greeter.BeginScope();

    [Fact]
    public async Task ShouldGreetByName()
    {
        var name = Guid.NewGuid().ToString();

        _greeter.Respond(client => client.SayHelloAsync(new HelloRequest { Name = name }),
            new HelloReply { Message = "Hi" });

        await fixture.Http.CreateClient().PostAsync($"/greetings/{name}", null);

        Assert.Single(_greeter.Requests<HelloRequest>(request => request.Name == name));
    }

    public void Dispose() => _greeter.Dispose();
}
```

- A scope has the members of the harness. The stubs made through it are removed when it is disposed; the stubs made
  through the harness, the defaults of the fixture, stay.
- `Requests` and `WaitForRequestAsync` of a scope see only the requests received since the scope began.
- Stub by an id of the test's own, as above: a stub without a condition made in one test answers the calls the other
  tests cause while it exists. Scopes of tests that run at the same time do not remove each other's stubs, but each
  sees the requests of the others, so filter the requests by the same id.
- The server keeps every request it received. In a long run, bound the log:
  `ConfigureServer = settings => settings.MaxRequestLogCount = 1000`.

### Waiting for a call

When the application calls the service later (after it consumed a message, on a timer), wait for the call instead
of a delay:

```csharp
var request = await greeter.WaitForRequestAsync(
    client => client.SayHelloAsync(null), (HelloRequest request) => request.Name == name, TimeSpan.FromSeconds(10));
```

It returns as soon as a matching request arrived, also one that arrived before the wait began, and throws a
`TimeoutException` that tells how many requests of the method arrived and shows the last one. Without a predicate
the request type cannot be inferred: write
`WaitForRequestAsync<HelloRequest, HelloReply>(client => client.SayHelloAsync(null), timeout)`.

### Several services on one address

For a backend that hosts several gRPC services, use `WireMockGrpcHarness` and take each service by its client:

```csharp
var backend = new WireMockGrpcHarness { ServiceName = "backend" };
```

```csharp
backend.Service<Greeter.GreeterClient>().Respond(client => client.SayHelloAsync(null), reply);
backend.Service<Clock.ClockClient>().Respond(client => client.GetTimeAsync(null), time);
```

### Stubs with WireMock's builders

For what `Respond` and `Fail` do not cover (delays, scenarios, trailers, calls without generated code), describe
the call and the response with the extensions of WireMock's own builders. They work on the server of any harness
that listens over HTTP/2: a `WireMockGrpcHarness`, or a `WireMockHarness` with `UseHttp2 = true`.

```csharp
greeter.Server
       .Given(Request.Create()
                     .UsingGrpc(Greeter.Descriptor, nameof(Greeter.GreeterClient.SayHelloAsync))
                     .WithGrpcMessage<HelloRequest>(request => request.Name == "world"))
       .RespondWith(Response.Create().WithGrpcMessage(new HelloReply { Message = "Hello, world" }));
```

- `UsingGrpc(service, method)` takes the service descriptor from the generated code and a method name. Name the
  method with `nameof` on the generated client, so a wrong name does not compile. Without generated code, pass the
  full method name as a string: `UsingGrpc("greet.Greeter/SayHello")`.
- `WithGrpcMessage<TRequest>(predicate)` matches the request message; leave it out to match every call of the method.
- `WithGrpcMessage(message)` responds with a message, and `WithGrpcStatus(StatusCode.NotFound, "...")` fails the
  call with a status.
- `WithGrpcStatus(code, message, details)` adds error details to the status and `WithGrpcTrailer(key, value)` adds a
  trailer; see [Error details and trailers](#error-details-and-trailers).
- A call that no stub matches fails on the client with the `Unimplemented` status. The status detail tells whether
  the method has stubs and shows the request message, for example
  `the request did not satisfy the 1 stub(s) of this method. Request: { "name": "someone else" }`.
- A stub whose message type is not the one the method takes throws when it is created, instead of never matching.
- The extensions cover unary calls, not streaming.
- A server with `UseHttp2` does not answer HTTP/1.1 requests. Use one harness for gRPC and another for HTTP APIs.

### Error details and trailers

A service that follows the gRPC [rich error model](https://grpc.io/docs/guides/error/#richer-error-model) sends a
`google.rpc.Status` with details next to the status code. Pass the details to `WithGrpcStatus`:

```csharp
greeter.Server
       .Given(Request.Create().UsingGrpc(Greeter.Descriptor, nameof(Greeter.GreeterClient.SayHelloAsync)))
       .RespondWith(Response.Create()
                            .WithGrpcStatus(StatusCode.FailedPrecondition, "The person is blocked",
                                new ErrorInfo { Reason = "PERSON_BLOCKED", Domain = "greeter" }));
```

The application reads them as it does from the real service, for example with `GetRpcStatus()` of the
`Grpc.StatusProto` package:

```csharp
var error = exception.GetRpcStatus()?.GetDetail<ErrorInfo>();
```

- A detail is any protobuf message: a standard one such as `ErrorInfo` (package `Google.Api.CommonProtos`) or a
  message of the service's own. Each one is packed into `Any`; an `Any` is sent as it is.
- To send a `Google.Rpc.Status` you already have, pass its parts:
  `WithGrpcStatus((StatusCode)status.Code, status.Message, [.. status.Details])`.
- `WithGrpcTrailer(key, value)` adds any other trailer, after a message or with a failure status. A text value is
  printable ASCII; a trailer whose name ends with `-bin` takes bytes and is sent base64-encoded, as gRPC requires.
  The client reads trailers from `RpcException.Trailers`, or from `GetTrailers()` of a call that succeeded.
  Names that start with `grpc-` are reserved: set the status, its message and its details with `WithGrpcStatus`.
- `WithGrpcStatus` called again replaces the status set before, with its message and details.

```csharp
Response.Create()
        .WithGrpcStatus(StatusCode.PermissionDenied, "Blocked")
        .WithGrpcTrailer("error-code", "PERSON_BLOCKED")
        .WithGrpcTrailer("signature-bin", signature);
```

The package depends on `WireMock.Net.Minimal`. Add the full `WireMock.Net` package if you need its extra matchers
(GraphQL, ProtoBuf).
