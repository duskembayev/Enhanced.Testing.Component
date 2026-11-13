# Enhanced.Testing.Component.GrpcClient

[![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component.GrpcClient)](https://www.nuget.org/packages/Enhanced.Testing.Component.GrpcClient)

A gRPC client harness for [Enhanced.Testing.Component](https://github.com/duskembayev/Enhanced.Testing.Component):
creates generated gRPC clients that call the application in memory through `TestServer`, without opening network
ports.

```shell
dotnet add package Enhanced.Testing.Component.GrpcClient
```

## Usage

```csharp
var grpc = new GrpcClientHarness();

var component = ComponentBuilder.Create<Program>()
                                .AddHarness(grpc)
                                .Build();
```

In a test, create any client generated from your `.proto` files:

```csharp
var client = grpc.CreateClient<Greeter.GreeterClient>();

var reply = await client.SayHelloAsync(new HelloRequest { Name = "John" });

Assert.Equal("Hello John", reply.Message);
```

The harness creates one channel per client type and shuts the channels down when the component stops.

Generate the client from the same `.proto` file as the service, for example in the test project:

```xml
<ItemGroup>
  <Protobuf Include="..\MyService\Protos\greet.proto" GrpcServices="Client" />
</ItemGroup>
```
