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

The test project usually references the service project, so generate the client there, next to the server code,
and use it from the tests:

```xml
<ItemGroup>
  <Protobuf Include="Protos\greet.proto" GrpcServices="Both" />
</ItemGroup>
```

Don't generate the same `.proto` file again in the test project: the generated types would exist twice and the build
reports `CS0436` conflicts. If the test project doesn't reference the service project, generate the client there
instead (`GrpcServices="Client"`, with the `Grpc.Tools` and `Google.Protobuf` packages, or `Grpc.AspNetCore`, which
includes both).
