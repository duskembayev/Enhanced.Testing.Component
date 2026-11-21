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

The same harness fakes a gRPC service the application calls. Set `UseHttp2`, and describe calls and responses with
the generated message types:

```csharp
var greeter = new WireMockHarness { ConfigurationKey = "Greeter:Address", UseHttp2 = true };
```

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
- A server with `UseHttp2` does not answer HTTP/1.1 requests. Use one harness for gRPC and another for HTTP APIs.

Streaming calls use the same extensions with several messages:

```csharp
greeter.Server
       .Given(Request.Create().UsingGrpc(Greeter.Descriptor, nameof(Greeter.GreeterClient.SayHellos)))
       .RespondWith(Response.Create()
                            .WithGrpcMessages(new HelloReply { Message = "first" }, new HelloReply { Message = "second" })
                            .WithGrpcStatus(StatusCode.Aborted, "The stream broke."));
```

- `WithGrpcMessages(messages)` responds to a server streaming call; with `WithGrpcStatus` the call fails once the
  client has read the messages. Error details and trailers arrive after the messages too.
- `WithGrpcMessages<TRequest>(predicate)` matches a client streaming call by all the messages the client sent.
- The server answers when the client has completed its request, and sends all response messages together. A duplex
  call works when the client sends everything before it reads; a call in which the two sides take turns does not.

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

```csharp
Response.Create()
        .WithGrpcStatus(StatusCode.PermissionDenied, "Blocked")
        .WithGrpcTrailer("error-code", "PERSON_BLOCKED")
        .WithGrpcTrailer("signature-bin", signature);
```

The package depends on `WireMock.Net.Minimal`. Add the full `WireMock.Net` package if you need its extra matchers
(GraphQL, ProtoBuf).
