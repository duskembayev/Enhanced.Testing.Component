using Enhanced.Testing.Component;
using Enhanced.Testing.Component.Tests.Protos;
using Enhanced.Testing.Component.WireMock;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Grpc.Net.Client;
using SampleService;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

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
        _shop.On<GetItemRequest>("shop.Catalog/GetItem").Respond(new Item { Name = "Got" });
        _shop.On<GetItemRequest>(Catalog.Descriptor, nameof(Catalog.CatalogClient.FindItemAsync), request => request.Id == "1")
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
        Assert.EndsWith("Name the method in On.", sameMessages.Message);

        var withoutResponse = Assert.Throws<ArgumentException>(
            () => _shop.On<GetItemRequest>().Fail(StatusCode.NotFound));

        Assert.Contains("GetPrice", withoutResponse.Message);

        var twoServices = Assert.Throws<ArgumentException>(() => _shop.On<DeleteItemRequest>().Respond(new Empty()));

        Assert.Contains("shop.Catalog/DeleteItem", twoServices.Message);
        Assert.Contains("shop.Stock/DeleteItem", twoServices.Message);
        Assert.EndsWith("Name the service in On.", twoServices.Message);

        var named = Assert.Throws<ArgumentException>(
            () => _shop.On<GetItemRequest>(Catalog.Descriptor).Fail(StatusCode.NotFound));

        Assert.EndsWith("Name the method in On.", named.Message);
    }

    [Fact]
    public void ShouldRejectCallThatNoMethodFits()
    {
        var noMethod = Assert.Throws<ArgumentException>(() => _shop.On<Item>());

        Assert.Contains("takes Item messages", noMethod.Message);
        Assert.Contains("shop.Stock/GetStock(GetStockRequest) returns StockLevel", noMethod.Message);

        var streaming = Assert.Throws<ArgumentException>(() => _shop.On<WatchItemsRequest>());

        Assert.Contains("No unary gRPC method", streaming.Message);
        Assert.Contains("Streaming methods take them", streaming.Message);
        Assert.Contains("shop.Catalog/WatchItems(WatchItemsRequest)", streaming.Message);

        var wrongName = Assert.Throws<ArgumentException>(() => _shop.On<GetStockRequest>("GetItem"));

        Assert.Contains("'GetItem'", wrongName.Message);

        var wrongService = Assert.Throws<ArgumentException>(() => _shop.On<GetStockRequest>(Catalog.Descriptor));

        Assert.Contains("of the service 'shop.Catalog'", wrongService.Message);
        Assert.Contains("Other services have such a method: shop.Stock/GetStock", wrongService.Message);
        Assert.Throws<ArgumentException>(() => _shop.On<GetStockRequest>(" "));

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
    public async Task ShouldReturnRequestsOfMethod()
    {
        _shop.On<GetItemRequest>("GetItem").Respond(new Item());
        _shop.On<GetStockRequest>().Respond(new StockLevel());

        await CatalogClient.GetItemAsync(new GetItemRequest { Id = "1" });
        await StockClient.GetStockAsync(new GetStockRequest { ItemId = "2" });
        await CatalogClient.GetItemAsync(new GetItemRequest { Id = "3" });
        await Assert.ThrowsAsync<RpcException>(async () => await CatalogClient.FindItemAsync(new GetItemRequest { Id = "4" }));

        var got = _shop.On<GetItemRequest>("GetItem").Requests();
        var third = _shop.On<GetItemRequest>("GetItem", request => request.Id == "3").Requests();
        var unanswered = _shop.On<GetItemRequest>(nameof(Catalog.CatalogClient.FindItemAsync)).Requests();

        Assert.Equal(["1", "3"], got.Select(request => request.Id).ToArray());
        Assert.Equal("3", Assert.Single(third).Id);
        Assert.Equal("4", Assert.Single(unanswered).Id);
        Assert.Equal("2", Assert.Single(_shop.On<GetStockRequest>().Requests()).ItemId);
        Assert.Empty(_shop.On<GetItemRequest>("GetPrice").Requests());

        var exception = Assert.Throws<ArgumentException>(() => _shop.On<GetItemRequest>().Requests());

        Assert.EndsWith("Name the method in On.", exception.Message);
    }

    [Fact]
    public async Task ShouldWaitForRequestThatArrivesLater()
    {
        _shop.On<GetStockRequest>().Respond(new StockLevel());

        var wait = _shop.On<GetStockRequest>(request => request.ItemId == "late")
                        .WaitForRequestAsync(TimeSpan.FromSeconds(10));

        await StockClient.GetStockAsync(new GetStockRequest { ItemId = "other" });
        await Task.Delay(100);

        Assert.False(wait.IsCompleted);

        await StockClient.GetStockAsync(new GetStockRequest { ItemId = "late" });

        Assert.Equal("late", (await wait).ItemId);
    }

    [Fact]
    public async Task ShouldReturnRequestThatArrivedBeforeWait()
    {
        _shop.On<GetStockRequest>().Respond(new StockLevel());
        await StockClient.GetStockAsync(new GetStockRequest { ItemId = "early" });

        var request = await _shop.On<GetStockRequest>().WaitForRequestAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("early", request.ItemId);
    }

    [Fact]
    public async Task ShouldReportRequestsWhenWaitTimesOut()
    {
        _shop.On<GetStockRequest>().Respond(new StockLevel());
        await StockClient.GetStockAsync(new GetStockRequest { ItemId = "another" });

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => _shop.On<GetStockRequest>(request => request.ItemId == "mine")
                       .WaitForRequestAsync(TimeSpan.FromMilliseconds(200)));

        Assert.Contains("shop.Stock/GetStock", exception.Message);
        Assert.Contains("that satisfies the condition arrived within 200 ms", exception.Message);
        Assert.Contains("The request log has 1 request(s) of this method", exception.Message);
        Assert.Contains("\"itemId\": \"another\"", exception.Message);

        var none = await Assert.ThrowsAsync<TimeoutException>(
            () => _shop.On<GetItemRequest>("GetPrice").WaitForRequestAsync(TimeSpan.FromMilliseconds(50)));

        Assert.EndsWith("The request log has 0 request(s) of this method.", none.Message);
        await Assert.ThrowsAsync<ArgumentException>(
            () => _shop.On<GetItemRequest>().WaitForRequestAsync(TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public async Task ShouldRemoveStubWhenDisposed()
    {
        _shop.On<GetStockRequest>().Respond(new StockLevel { Quantity = 100 });

        using (_shop.On<GetStockRequest>().Fail(StatusCode.Unavailable))
        {
            await Assert.ThrowsAsync<RpcException>(
                async () => await StockClient.GetStockAsync(new GetStockRequest { ItemId = "1" }));
        }

        var stub = _shop.On<GetStockRequest>(request => request.ItemId == "1").Respond(new StockLevel { Quantity = 1 });

        Assert.Equal(1, (await StockClient.GetStockAsync(new GetStockRequest { ItemId = "1" })).Quantity);

        stub.Dispose();
        stub.Dispose();

        Assert.Equal(100, (await StockClient.GetStockAsync(new GetStockRequest { ItemId = "1" })).Quantity);
    }

    [Fact]
    public async Task ShouldPreferStubMadeThroughServer()
    {
        _shop.On<GetStockRequest>(request => request.ItemId == "1").Respond(new StockLevel { Quantity = 1 });
        _shop.Server
             .Given(Request.Create().UsingGrpc(Stock.Descriptor, "GetStock"))
             .RespondWith(Response.Create().WithGrpcMessage(new StockLevel { Quantity = 500 }));

        Assert.Equal(500, (await StockClient.GetStockAsync(new GetStockRequest { ItemId = "1" })).Quantity);
    }

    [Fact]
    public async Task ShouldNotSelectRequestWhenPredicateThrows()
    {
        Func<GetStockRequest, bool> throwing =
            request => request.ItemId == "bad" ? throw new InvalidOperationException("No such field") : true;

        _shop.On(throwing).Respond(new StockLevel { Quantity = 1 });

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await StockClient.GetStockAsync(new GetStockRequest { ItemId = "bad" }));

        Assert.Equal(StatusCode.Unimplemented, exception.StatusCode);
        Assert.Contains("The condition of a stub threw InvalidOperationException: No such field", exception.Status.Detail);

        await StockClient.GetStockAsync(new GetStockRequest { ItemId = "good" });

        Assert.Equal("good", Assert.Single(_shop.On(throwing).Requests()).ItemId);

        Func<GetStockRequest, bool> never =
            request => request.ItemId == "bad" ? throw new InvalidOperationException("No such field") : false;
        var timeout = await Assert.ThrowsAsync<TimeoutException>(
            () => _shop.On(never).WaitForRequestAsync(TimeSpan.FromMilliseconds(100)));

        Assert.Contains("The request log has 2 request(s) of this method", timeout.Message);
        Assert.Contains("The condition threw InvalidOperationException for 1 of them: No such field", timeout.Message);
    }

    [Fact]
    public async Task ShouldReportRequestsThatCannotBeRead()
    {
        _shop.On<GetStockRequest>().Respond(new StockLevel());

        await StockClient.GetStockAsync(new GetStockRequest { ItemId = "compressed" },
            new Metadata { { "grpc-internal-encoding-request", "gzip" } });

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => _shop.On<GetStockRequest>().WaitForRequestAsync(TimeSpan.Zero));

        Assert.Empty(_shop.On<GetStockRequest>().Requests());
        Assert.Contains("The request log has 0 request(s) of this method and 1 more that could not be read",
            exception.Message);
    }

    [Fact]
    public async Task ShouldShortenRequestInTimeoutMessage()
    {
        _shop.On<GetStockRequest>().Respond(new StockLevel());
        await StockClient.GetStockAsync(new GetStockRequest { ItemId = new string('ж', 5000) });

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => _shop.On<GetStockRequest>(request => request.ItemId == "mine").WaitForRequestAsync(TimeSpan.Zero));

        Assert.InRange(exception.Message.Length, 500, 800);
        Assert.Equal(5000, Assert.Single(_shop.On<GetStockRequest>().Requests()).ItemId.Length);
    }

    [Fact]
    public async Task ShouldWaitWithoutLimitUntilCancelled()
    {
        _shop.On<GetStockRequest>().Respond(new StockLevel());

        using var cancellation = new CancellationTokenSource();
        var cancelled = _shop.On<GetStockRequest>(request => request.ItemId == "never")
                             .WaitForRequestAsync(Timeout.InfiniteTimeSpan, cancellation.Token);
        var wait = _shop.On<GetStockRequest>(request => request.ItemId == "late")
                        .WaitForRequestAsync(Timeout.InfiniteTimeSpan);

        await Task.Delay(100);

        Assert.False(cancelled.IsCompleted);
        Assert.False(wait.IsCompleted);

        cancellation.Cancel();
        await StockClient.GetStockAsync(new GetStockRequest { ItemId = "late" });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal("late", (await wait).ItemId);
        Assert.Equal("late", (await _shop.On<GetStockRequest>().WaitForRequestAsync(TimeSpan.Zero)).ItemId);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _shop.On<GetStockRequest>().WaitForRequestAsync(TimeSpan.FromSeconds(-5)));
    }

    [Fact]
    public void ShouldRejectStubThatCannotWork()
    {
        var exception = Assert.Throws<ArgumentException>(() => _shop.On<GetStockRequest>().Fail(StatusCode.OK));

        Assert.Contains("Respond", exception.Message);
        Assert.Throws<ArgumentNullException>(() => _shop.On<GetStockRequest>().Respond(null!));
        Assert.Throws<ArgumentNullException>(() => _shop.On<GetStockRequest>().Fail(StatusCode.NotFound, "x", null!));
        Assert.Throws<ArgumentNullException>(() => _shop.On<GetStockRequest>((Google.Protobuf.Reflection.ServiceDescriptor)null!));
        Assert.Single(_shop.Server.Mappings);
    }

    [Fact]
    public async Task ShouldRejectServerThatDoesNotListenOverHttp2()
    {
        var byProperty = new WireMockGrpcHarness(Stock.Descriptor) { UseHttp2 = false };
        var bySettings = new WireMockGrpcHarness(Stock.Descriptor) { ConfigureServer = settings => settings.UseHttp2 = false };

        foreach (var harness in new[] { byProperty, bySettings })
        {
            var component = ComponentBuilder.Create<Program>().AddHarness(harness).Build();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => component.StartAsync(default));

            Assert.Contains("HTTP/2", exception.Message);
        }
    }

    [Fact]
    public async Task ShouldKeepTextThatIsNotAscii()
    {
        _shop.On<GetStockRequest>(request => request.ItemId == "Zoë").Fail(StatusCode.NotFound, "Нет такого товара: Zoë");

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await StockClient.GetStockAsync(new GetStockRequest { ItemId = "Zoë" }));

        Assert.Equal("Нет такого товара: Zoë", exception.Status.Detail);
        Assert.Equal("Zoë", Assert.Single(_shop.On<GetStockRequest>().Requests()).ItemId);
    }

    [Fact]
    public async Task ShouldStubAgainAfterReset()
    {
        _shop.On<GetStockRequest>().Respond(new StockLevel { Quantity = 1 });
        await StockClient.GetStockAsync(new GetStockRequest { ItemId = "0" });
        _shop.Reset();

        Assert.Empty(_shop.On<GetStockRequest>().Requests());

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
