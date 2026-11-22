using Enhanced.Testing.Component;
using Enhanced.Testing.Component.Tests.Protos;
using Enhanced.Testing.Component.WireMock;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Grpc.Net.Client;
using SampleService;

namespace Enhanced.Testing.Component.Tests;

public class WireMockGrpcHarnessTests : IAsyncLifetime
{
    private readonly WireMockGrpcHarness _shop = new(Catalog.Descriptor, Stock.Descriptor);
    private readonly IComponent _component;
    private GrpcChannel? _channel;

    public WireMockGrpcHarnessTests()
    {
        _component = ComponentBuilder.Create<Program>()
                                     .AddHarness(_shop)
                                     .Build();
    }

    private Catalog.CatalogClient CatalogClient => new(_channel!);

    private Stock.StockClient StockClient => new(_channel!);

    [Fact]
    public async Task ShouldRespondToEveryCallOfMethod()
    {
        _shop.On<GetStockRequest>().Respond(new StockLevel { Quantity = 7 });

        var first = await StockClient.GetStockAsync(new GetStockRequest { ItemId = "1" });
        var second = await StockClient.GetStockAsync(new GetStockRequest { ItemId = "2" });

        Assert.Equal(7, first.Quantity);
        Assert.Equal(7, second.Quantity);
    }

    [Fact]
    public async Task ShouldRespondWhenPredicateMatches()
    {
        _shop.On<GetStockRequest>(request => request.ItemId == "1").Respond(new StockLevel { Quantity = 1 });

        var level = await StockClient.GetStockAsync(new GetStockRequest { ItemId = "1" });
        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await StockClient.GetStockAsync(new GetStockRequest { ItemId = "2" }));

        Assert.Equal(1, level.Quantity);
        Assert.Equal(StatusCode.Unimplemented, exception.StatusCode);
        Assert.Contains("did not satisfy the 1 stub(s)", exception.Status.Detail);
        Assert.Contains("\"itemId\": \"2\"", exception.Status.Detail);
    }

    [Fact]
    public async Task ShouldPreferStubWithCondition()
    {
        _shop.On<GetStockRequest>(request => request.ItemId == "1").Respond(new StockLevel { Quantity = 1 });
        _shop.On<GetStockRequest>().Respond(new StockLevel { Quantity = 100 });

        Assert.Equal(1, (await StockClient.GetStockAsync(new GetStockRequest { ItemId = "1" })).Quantity);
        Assert.Equal(100, (await StockClient.GetStockAsync(new GetStockRequest { ItemId = "2" })).Quantity);
    }

    [Fact]
    public async Task ShouldPreferLastOfEqualStubs()
    {
        _shop.On<GetStockRequest>().Respond(new StockLevel { Quantity = 100 });
        _shop.On<GetStockRequest>().Respond(new StockLevel { Quantity = 200 });
        _shop.On<GetStockRequest>(request => request.ItemId == "1").Respond(new StockLevel { Quantity = 1 });
        _shop.On<GetStockRequest>(request => request.ItemId == "1").Fail(StatusCode.NotFound);
        _shop.On<GetStockRequest>(request => request.ItemId == "1").Respond(new StockLevel { Quantity = 3 });

        Assert.Equal(3, (await StockClient.GetStockAsync(new GetStockRequest { ItemId = "1" })).Quantity);
        Assert.Equal(200, (await StockClient.GetStockAsync(new GetStockRequest { ItemId = "2" })).Quantity);
    }

    [Fact]
    public async Task ShouldChooseMethodByResponseType()
    {
        _shop.On<GetItemRequest>().Respond(new Price { Cents = 250 });

        var price = await CatalogClient.GetPriceAsync(new GetItemRequest { Id = "1" });

        Assert.Equal(250, price.Cents);
        await Assert.ThrowsAsync<RpcException>(async () => await CatalogClient.GetItemAsync(new GetItemRequest { Id = "1" }));
    }

    [Fact]
    public async Task ShouldChooseMethodByName()
    {
        _shop.On<GetItemRequest>("GetItem").Respond(new Item { Name = "Got" });
        _shop.On<GetItemRequest>(nameof(Catalog.CatalogClient.FindItemAsync), request => request.Id == "1")
             .Respond(new Item { Name = "Found" });
        _shop.On<GetItemRequest>("GetPrice").Fail(StatusCode.NotFound, "No price");

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await CatalogClient.GetPriceAsync(new GetItemRequest { Id = "1" }));

        Assert.Equal("Got", (await CatalogClient.GetItemAsync(new GetItemRequest { Id = "1" })).Name);
        Assert.Equal("Found", (await CatalogClient.FindItemAsync(new GetItemRequest { Id = "1" })).Name);
        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
        await Assert.ThrowsAsync<RpcException>(async () => await CatalogClient.FindItemAsync(new GetItemRequest { Id = "2" }));
    }

    [Fact]
    public async Task ShouldChooseMethodByService()
    {
        _shop.On<DeleteItemRequest>(Stock.Descriptor).Respond(new Empty());
        _shop.On<DeleteItemRequest>(Catalog.Descriptor, "DeleteItem", request => request.Id == "locked")
             .Fail(StatusCode.PermissionDenied);
        _shop.On<GetItemRequest>(Catalog.Descriptor, request => request.Id == "1").Respond(new Price { Cents = 1 });

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await CatalogClient.DeleteItemAsync(new DeleteItemRequest { Id = "locked" }));

        Assert.NotNull(await StockClient.DeleteItemAsync(new DeleteItemRequest { Id = "locked" }));
        Assert.Equal(StatusCode.PermissionDenied, exception.StatusCode);
        Assert.Equal(1, (await CatalogClient.GetPriceAsync(new GetItemRequest { Id = "1" })).Cents);
    }

    [Fact]
    public void ShouldRejectCallThatSeveralMethodsFit()
    {
        var sameMessages = Assert.Throws<ArgumentException>(() => _shop.On<GetItemRequest>().Respond(new Item()));

        Assert.Contains("shop.Catalog/GetItem(GetItemRequest) returns Item", sameMessages.Message);
        Assert.Contains("shop.Catalog/FindItem(GetItemRequest) returns Item", sameMessages.Message);
        Assert.DoesNotContain("GetPrice", sameMessages.Message);
        Assert.Contains("Name the method or the service", sameMessages.Message);

        var withoutResponse = Assert.Throws<ArgumentException>(
            () => _shop.On<GetItemRequest>().Fail(StatusCode.NotFound));

        Assert.Contains("GetPrice", withoutResponse.Message);

        var twoServices = Assert.Throws<ArgumentException>(() => _shop.On<DeleteItemRequest>().Respond(new Empty()));

        Assert.Contains("shop.Catalog/DeleteItem", twoServices.Message);
        Assert.Contains("shop.Stock/DeleteItem", twoServices.Message);
    }

    [Fact]
    public void ShouldRejectCallThatNoMethodFits()
    {
        var noMethod = Assert.Throws<ArgumentException>(() => _shop.On<Item>());

        Assert.Contains("takes Item messages", noMethod.Message);
        Assert.Contains("shop.Stock/GetStock(GetStockRequest) returns StockLevel", noMethod.Message);

        var streaming = Assert.Throws<ArgumentException>(() => _shop.On<WatchItemsRequest>());

        Assert.Contains("No unary gRPC method", streaming.Message);

        var wrongName = Assert.Throws<ArgumentException>(() => _shop.On<GetStockRequest>("GetItem"));

        Assert.Contains("'GetItem'", wrongName.Message);

        var wrongService = Assert.Throws<ArgumentException>(() => _shop.On<GetStockRequest>(Catalog.Descriptor));

        Assert.DoesNotContain("shop.Stock", wrongService.Message);

        var wrongResponse = Assert.Throws<ArgumentException>(() => _shop.On<GetStockRequest>().Respond(new Item()));

        Assert.Contains("returns Item messages", wrongResponse.Message);
        Assert.Contains("shop.Stock/GetStock(GetStockRequest) returns StockLevel", wrongResponse.Message);

        var foreignService = Assert.Throws<ArgumentException>(() => _shop.On<HelloRequest>(Greeter.Descriptor));

        Assert.Contains("greet.Greeter", foreignService.Message);
        Assert.Contains("shop.Catalog, shop.Stock", foreignService.Message);
    }

    [Fact]
    public async Task ShouldFailCall()
    {
        var error = new ErrorInfo { Reason = "OUT_OF_STOCK", Domain = "shop" };

        _shop.On<GetStockRequest>().Fail(StatusCode.FailedPrecondition, "Nothing left", error);

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await StockClient.GetStockAsync(new GetStockRequest { ItemId = "1" }));

        Assert.Equal(StatusCode.FailedPrecondition, exception.StatusCode);
        Assert.Equal("Nothing left", exception.Status.Detail);
        Assert.Equal(error, exception.GetRpcStatus()?.GetDetail<ErrorInfo>());
    }

    [Fact]
    public async Task ShouldStubAgainAfterReset()
    {
        _shop.On<GetStockRequest>().Respond(new StockLevel { Quantity = 1 });
        _shop.Reset();

        await Assert.ThrowsAsync<RpcException>(
            async () => await StockClient.GetStockAsync(new GetStockRequest { ItemId = "1" }));

        _shop.On<GetStockRequest>().Respond(new StockLevel { Quantity = 2 });

        Assert.Equal(2, (await StockClient.GetStockAsync(new GetStockRequest { ItemId = "1" })).Quantity);
    }

    [Fact]
    public void ShouldRejectServicesThatDoNotMakeHarness()
    {
        Assert.Throws<ArgumentException>(() => new WireMockGrpcHarness());

        var exception = Assert.Throws<ArgumentException>(
            () => new WireMockGrpcHarness(Catalog.Descriptor, Stock.Descriptor, Catalog.Descriptor));

        Assert.Contains("shop.Catalog", exception.Message);
        Assert.Equal([Catalog.Descriptor, Stock.Descriptor], _shop.Services);
        Assert.True(_shop.UseHttp2);
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _component.StartAsync(default);
        _channel = GrpcChannel.ForAddress(_shop.BaseAddress);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        _channel?.Dispose();
        await _component.StopAsync(default);
    }
}
