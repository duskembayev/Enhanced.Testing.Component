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

`WireMockGrpcHarness` fakes a gRPC backend the application calls. It takes the descriptors of the services from the
generated code, and a stub names a call by its request message type:

```csharp
var backend = new WireMockGrpcHarness(Greeter.Descriptor) { ServiceName = "greeter" };
```

```csharp
backend.On<HelloRequest>().Respond(new HelloReply { Message = "Hello" });
backend.On<HelloRequest>(request => request.Name == "nobody").Fail(StatusCode.NotFound, "No such person");
```

- `On<TRequest>()` selects the calls of the method that takes `TRequest` messages; with a predicate, the calls whose
  request satisfies it.
- `Respond(message)` answers them with the message, and `Fail(statusCode, message, details)` fails them; the details
  make a rich status, see [Error details and trailers](#error-details-and-trailers).
- `Requests()` returns the request messages the application sent, to assert on them, and
  `WaitForRequestAsync(timeout)` waits for a call the application makes later; see
  [What the application sent](#what-the-application-sent).
- When several stubs fit a call, a stub with a predicate answers before a stub without one, and among equal stubs
  the one made last answers. So a fixture can set a default answer and a test can add its own for the request it
  sends.
- A stub that does not fit the services throws when it is made: no method takes the request type, or the method
  does not return messages of the response type. The message lists the methods.
- The server listens over HTTP/2. `ServiceName`, `EndpointName` and `ConfigurationKey` work as on `WireMockHarness`;
  for a client that calls `http://_grpc.greeter`, set `EndpointName = "grpc"`.
- Unary methods only.

### Methods that share a request type

The harness can fake several services on one address: pass all their descriptors. If several methods take the same
request type, the type of the response tells them apart. If it does not, or the call fails and so has no response,
name the method, the service, or both:

```csharp
var shop = new WireMockGrpcHarness(Catalog.Descriptor, Stock.Descriptor) { ServiceName = "shop" };
```

```csharp
shop.On<GetItemRequest>().Respond(new Price { Cents = 250 });
shop.On<GetItemRequest>(nameof(Catalog.CatalogClient.FindItemAsync)).Respond(item);
shop.On<DeleteItemRequest>(Stock.Descriptor).Respond(new Empty());
shop.On<DeleteItemRequest>(Catalog.Descriptor, "DeleteItem", request => request.Id == id).Fail(StatusCode.NotFound);
```

The method name is the one in the `.proto` file or the one of the client, with the `Async` suffix. A call that
several methods fit throws when the stub is made and lists them.

### What the application sent

```csharp
var sent = backend.On<HelloRequest>(request => request.Name == name).Requests();

Assert.Single(sent);
```

`Requests()` returns the requests of the selected calls in the order the server received them, also those that no
stub answered. When the application makes the call later (after it consumed a message, on a timer), wait for it
instead of a delay:

```csharp
var hello = await backend.On<HelloRequest>(request => request.Name == name)
                         .WaitForRequestAsync(TimeSpan.FromSeconds(10));
```

It returns as soon as a matching request arrived, also one that arrived before the wait began, and throws a
`TimeoutException` that tells how many requests of the method arrived and shows the last one.

- Both need one method: if several methods take the request type, name the method or the service in `On`.
- Tests that share the harness see each other's requests: select by an id of the test's own, as above.
- The server keeps every request until `Reset()`. In a long run without it, bound the log:
  `ConfigureServer = settings => settings.MaxRequestLogCount = 1000`.

### Stubs with WireMock's builders

For what `On` does not cover (delays, scenarios, trailers, calls without generated code), describe the call and the
response with the extensions of WireMock's own builders. They work on the server of any harness that listens over
HTTP/2: a `WireMockGrpcHarness`, or a `WireMockHarness` with `UseHttp2 = true`. Unless they are given a priority,
these stubs answer before the stubs made with `On`.

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
