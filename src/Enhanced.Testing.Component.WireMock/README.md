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

The package depends on `WireMock.Net.Minimal`. Add the full `WireMock.Net` package if you need its extra matchers
(GraphQL, ProtoBuf).
