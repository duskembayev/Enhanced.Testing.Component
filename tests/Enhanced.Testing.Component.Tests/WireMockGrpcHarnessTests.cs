using Enhanced.Testing.Component;
using Enhanced.Testing.Component.Tests.Protos;
using Enhanced.Testing.Component.WireMock;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Grpc.Net.Client;

namespace Enhanced.Testing.Component.Tests;

public class WireMockGrpcHarnessTests : IAsyncLifetime
{
    private readonly WireMockGrpcHarness<Catalog.CatalogClient> _catalog = new();
    private readonly WireMockGrpcHarness _shop = new();
    private readonly IComponent _component;
    private GrpcChannel? _catalogChannel;
    private GrpcChannel? _shopChannel;

    public WireMockGrpcHarnessTests()
    {
        _component = ComponentBuilder.Create<Program>()
                                     .AddHarness(_catalog)
                                     .AddHarness(_shop)
                                     .Build();
    }

    private Catalog.CatalogClient Client => new(_catalogChannel!);

    [Fact]
    public async Task ShouldRespondToEveryCallOfMethod()
    {
        _catalog.Respond(client => client.GetItemAsync(null), new Item { Id = "1", Name = "Book" });

        var first = await Client.GetItemAsync(new GetItemRequest { Id = "1" });
        var second = await Client.GetItemAsync(new GetItemRequest { Id = "2" });

        Assert.Equal("Book", first.Name);
        Assert.Equal("Book", second.Name);
    }

    [Fact]
    public async Task ShouldRespondToMethodThatSharesItsMessagesWithAnother()
    {
        _catalog.Respond(client => client.GetItemAsync(null), new Item { Name = "Got" });
        _catalog.Respond(client => client.FindItemAsync(null), new Item { Name = "Found" });
        _catalog.Respond(client => client.DeleteItemAsync(null), new Empty());

        Assert.Equal("Got", (await Client.GetItemAsync(new GetItemRequest { Id = "1" })).Name);
        Assert.Equal("Found", (await Client.FindItemAsync(new GetItemRequest { Id = "1" })).Name);
        Assert.NotNull(await Client.DeleteItemAsync(new DeleteItemRequest { Id = "1" }));
    }

    [Fact]
    public async Task ShouldRespondToCallWithEqualRequest()
    {
        var expected = new GetItemRequest { Id = "1" };

        _catalog.Respond(client => client.GetItemAsync(expected), new Item { Name = "Book" });
        expected.Id = "changed after the stub was made";

        var item = await Client.GetItemAsync(new GetItemRequest { Id = "1" });
        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await Client.GetItemAsync(new GetItemRequest { Id = "2" }));

        Assert.Equal("Book", item.Name);
        Assert.Equal(StatusCode.Unimplemented, exception.StatusCode);
        Assert.Contains("did not satisfy the 1 stub(s)", exception.Status.Detail);
        Assert.Contains("\"id\": \"2\"", exception.Status.Detail);
    }

    [Fact]
    public async Task ShouldRespondWhenPredicateMatches()
    {
        _catalog.Respond(client => client.GetItemAsync(null), (GetItemRequest request) => request.Id.StartsWith('a'),
            new Item { Name = "Apple" });
        _catalog.Respond(client => client.GetItemAsync(new GetItemRequest { Id = "b1" }),
            (GetItemRequest request) => request.Id.EndsWith('1'), new Item { Name = "Banana" });

        Assert.Equal("Apple", (await Client.GetItemAsync(new GetItemRequest { Id = "a1" })).Name);
        Assert.Equal("Banana", (await Client.GetItemAsync(new GetItemRequest { Id = "b1" })).Name);
        await Assert.ThrowsAsync<RpcException>(async () => await Client.GetItemAsync(new GetItemRequest { Id = "b2" }));
    }

    [Fact]
    public async Task ShouldPreferStubWithCondition()
    {
        _catalog.Respond(client => client.GetItemAsync(new GetItemRequest { Id = "1" }), new Item { Name = "By request" });
        _catalog.Respond(client => client.GetItemAsync(null), (GetItemRequest request) => request.Id == "2",
            new Item { Name = "By predicate" });
        _catalog.Respond(client => client.GetItemAsync(null), new Item { Name = "Default" });

        Assert.Equal("By request", (await Client.GetItemAsync(new GetItemRequest { Id = "1" })).Name);
        Assert.Equal("By predicate", (await Client.GetItemAsync(new GetItemRequest { Id = "2" })).Name);
        Assert.Equal("Default", (await Client.GetItemAsync(new GetItemRequest { Id = "3" })).Name);
    }

    [Fact]
    public async Task ShouldPreferLastOfEqualStubs()
    {
        _catalog.Respond(client => client.GetItemAsync(null), new Item { Name = "First default" });
        _catalog.Respond(client => client.GetItemAsync(null), new Item { Name = "Second default" });
        _catalog.Respond(client => client.GetItemAsync(new GetItemRequest { Id = "1" }), new Item { Name = "First" });
        _catalog.Fail(client => client.GetItemAsync(new GetItemRequest { Id = "1" }), StatusCode.NotFound);
        _catalog.Respond(client => client.GetItemAsync(new GetItemRequest { Id = "1" }), new Item { Name = "Last" });

        Assert.Equal("Last", (await Client.GetItemAsync(new GetItemRequest { Id = "1" })).Name);
        Assert.Equal("Second default", (await Client.GetItemAsync(new GetItemRequest { Id = "2" })).Name);
    }

    [Fact]
    public async Task ShouldAnswerWithPreviousStubWhenStubIsDisposed()
    {
        _catalog.Respond(client => client.GetItemAsync(null), new Item { Name = "Default" });

        using (_catalog.Respond(client => client.GetItemAsync(null), new Item { Name = "Override" }))
        {
            Assert.Equal("Override", (await Client.GetItemAsync(new GetItemRequest { Id = "1" })).Name);
        }

        Assert.Equal("Default", (await Client.GetItemAsync(new GetItemRequest { Id = "1" })).Name);
    }

    [Fact]
    public async Task ShouldFailCall()
    {
        var error = new ErrorInfo { Reason = "ITEM_ARCHIVED", Domain = "catalog" };

        _catalog.Fail(client => client.GetItemAsync(null), StatusCode.FailedPrecondition, "The item is archived",
            error);

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await Client.GetItemAsync(new GetItemRequest { Id = "1" }));

        Assert.Equal(StatusCode.FailedPrecondition, exception.StatusCode);
        Assert.Equal("The item is archived", exception.Status.Detail);
        Assert.Equal(error, exception.GetRpcStatus()?.GetDetail<ErrorInfo>());
    }

    [Fact]
    public async Task ShouldFailCallWhenPredicateMatches()
    {
        _catalog.Respond(client => client.DeleteItemAsync(null), new Empty());
        _catalog.Fail(client => client.DeleteItemAsync(null), (DeleteItemRequest request) => request.Id == "locked",
            StatusCode.PermissionDenied);

        await Client.DeleteItemAsync(new DeleteItemRequest { Id = "free" });

        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await Client.DeleteItemAsync(new DeleteItemRequest { Id = "locked" }));

        Assert.Equal(StatusCode.PermissionDenied, exception.StatusCode);
    }

    [Fact]
    public async Task ShouldReturnRequestsOfType()
    {
        _catalog.Respond(client => client.GetItemAsync(null), new Item());
        _catalog.Respond(client => client.DeleteItemAsync(null), new Empty());

        await Client.GetItemAsync(new GetItemRequest { Id = "1" });
        await Client.DeleteItemAsync(new DeleteItemRequest { Id = "2" });
        await Client.GetItemAsync(new GetItemRequest { Id = "3" });
        await Assert.ThrowsAsync<RpcException>(async () => await Client.FindItemAsync(new GetItemRequest { Id = "4" }));

        Assert.Equal(["1", "3", "4"], _catalog.Requests<GetItemRequest>().Select(request => request.Id).ToArray());
        Assert.Equal("3", Assert.Single(_catalog.Requests<GetItemRequest>(request => request.Id == "3")).Id);
        Assert.Equal("2", Assert.Single(_catalog.Requests<DeleteItemRequest>()).Id);
    }

    [Fact]
    public async Task ShouldReturnRequestsOfCall()
    {
        _catalog.Respond(client => client.GetItemAsync(null), new Item());
        _catalog.Respond(client => client.FindItemAsync(null), new Item());

        await Client.GetItemAsync(new GetItemRequest { Id = "1" });
        await Client.FindItemAsync(new GetItemRequest { Id = "2" });
        await Client.GetItemAsync(new GetItemRequest { Id = "3" });

        var got = _catalog.Requests<GetItemRequest, Item>(client => client.GetItemAsync(null));
        var third = _catalog.Requests(client => client.GetItemAsync(null), (GetItemRequest request) => request.Id == "3");
        var equal = _catalog.Requests<GetItemRequest, Item>(client => client.FindItemAsync(new GetItemRequest { Id = "2" }));

        Assert.Equal(["1", "3"], got.Select(request => request.Id).ToArray());
        Assert.Equal("3", Assert.Single(third).Id);
        Assert.Equal("2", Assert.Single(equal).Id);
        Assert.Empty(_catalog.Requests<GetItemRequest, Item>(client => client.GetItemAsync(new GetItemRequest { Id = "2" })));
        Assert.Throws<ArgumentException>(
            () => _catalog.Requests<DeleteItemRequest, Item>(client => client.GetItemAsync(null)));
    }

    [Fact]
    public async Task ShouldRemoveStubsOfScopeAndKeepDefaults()
    {
        _catalog.Respond(client => client.GetItemAsync(null), new Item { Name = "Default" });

        using (var first = _catalog.BeginScope())
        {
            first.Respond(client => client.GetItemAsync(new GetItemRequest { Id = "1" }), new Item { Name = "First" });
            first.Service<Catalog.CatalogClient>().Respond(client => client.FindItemAsync(null), new Item { Name = "Found" });

            Assert.Equal("First", (await Client.GetItemAsync(new GetItemRequest { Id = "1" })).Name);
            Assert.Equal("Found", (await Client.FindItemAsync(new GetItemRequest { Id = "1" })).Name);
            Assert.Equal("1", Assert.Single(first.Requests<GetItemRequest, Item>(client => client.GetItemAsync(null))).Id);
        }

        using var second = _catalog.BeginScope();

        Assert.Empty(second.Requests<GetItemRequest>());
        Assert.Equal("Default", (await Client.GetItemAsync(new GetItemRequest { Id = "1" })).Name);
        await Assert.ThrowsAsync<RpcException>(async () => await Client.FindItemAsync(new GetItemRequest { Id = "2" }));

        Assert.Equal(["1", "2"], second.Requests<GetItemRequest>().Select(request => request.Id).ToArray());
        Assert.Equal(4, _catalog.Requests<GetItemRequest>().Count);
    }

    [Fact]
    public void ShouldRejectStubInDisposedScope()
    {
        var scope = _shop.BeginScope();
        var stock = scope.Service<Stock.StockClient>();

        stock.Respond(client => client.GetStockAsync(null), new StockLevel());
        scope.Dispose();
        scope.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => stock.Respond(client => client.GetStockAsync(null), new StockLevel()));
        Assert.Single(_shop.Server.Mappings);
    }

    [Fact]
    public async Task ShouldWaitForRequestThatArrivesLater()
    {
        _catalog.Respond(client => client.GetItemAsync(null), new Item());

        var wait = _catalog.WaitForRequestAsync(
            client => client.GetItemAsync(null), (GetItemRequest request) => request.Id == "late",
            TimeSpan.FromSeconds(10));

        await Client.GetItemAsync(new GetItemRequest { Id = "other" });
        await Task.Delay(100);

        Assert.False(wait.IsCompleted);

        await Client.GetItemAsync(new GetItemRequest { Id = "late" });

        Assert.Equal("late", (await wait).Id);
    }

    [Fact]
    public async Task ShouldReturnRequestThatArrivedBeforeWait()
    {
        _catalog.Respond(client => client.GetItemAsync(null), new Item());
        await Client.GetItemAsync(new GetItemRequest { Id = "early" });

        var byCall = await _catalog.WaitForRequestAsync<GetItemRequest, Item>(
            client => client.GetItemAsync(null), TimeSpan.FromSeconds(10));
        var byRequest = await _catalog.WaitForRequestAsync<GetItemRequest, Item>(
            client => client.GetItemAsync(new GetItemRequest { Id = "early" }), TimeSpan.FromSeconds(10));

        Assert.Equal("early", byCall.Id);
        Assert.Equal("early", byRequest.Id);
    }

    [Fact]
    public async Task ShouldReportRequestsWhenWaitTimesOut()
    {
        _catalog.Respond(client => client.GetItemAsync(null), new Item());
        await Client.GetItemAsync(new GetItemRequest { Id = "before the scope" });

        using var scope = _catalog.BeginScope();

        await Client.GetItemAsync(new GetItemRequest { Id = "another" });

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => scope.WaitForRequestAsync(
            client => client.GetItemAsync(null), (GetItemRequest request) => request.Id == "before the scope",
            TimeSpan.FromMilliseconds(200)));

        Assert.Contains("shop.Catalog/GetItem", exception.Message);
        Assert.Contains("200 ms", exception.Message);
        Assert.Contains("1 request(s) of this method arrived since the scope began", exception.Message);
        Assert.Contains("\"id\": \"another\"", exception.Message);

        var none = await Assert.ThrowsAsync<TimeoutException>(() => _catalog.WaitForRequestAsync<GetItemRequest, Item>(
            client => client.FindItemAsync(null), TimeSpan.FromMilliseconds(50)));

        Assert.EndsWith("0 request(s) of this method arrived.", none.Message);
    }

    [Fact]
    public async Task ShouldNotMatchWhenPredicateThrows()
    {
        _catalog.Respond(client => client.GetItemAsync(null), new Item { Name = "Default" });
        _catalog.Respond(client => client.GetItemAsync(null),
            (GetItemRequest request) => request.Id == "1" ? throw new InvalidOperationException("No such field") : false,
            new Item { Name = "Never" });
        _catalog.Respond(client => client.FindItemAsync(null),
            (GetItemRequest _) => throw new InvalidOperationException("No such field"), new Item());

        var answered = await Client.GetItemAsync(new GetItemRequest { Id = "1" });
        var exception = await Assert.ThrowsAsync<RpcException>(
            async () => await Client.FindItemAsync(new GetItemRequest { Id = "1" }));

        Assert.Equal("Default", answered.Name);
        Assert.Equal(StatusCode.Unimplemented, exception.StatusCode);
        Assert.Contains("The condition of a stub threw InvalidOperationException: No such field", exception.Status.Detail);
        Assert.Contains("\"id\": \"1\"", exception.Status.Detail);
    }

    [Fact]
    public void ShouldRejectRequestTypeThatNoMethodTakes()
    {
        var exception = Assert.Throws<ArgumentException>(() => _catalog.Requests<Item>());

        Assert.Contains("shop.Catalog", exception.Message);
        Assert.Contains("GetItem(GetItemRequest)", exception.Message);
    }

    [Fact]
    public async Task ShouldServeSeveralServices()
    {
        _shop.Service<Catalog.CatalogClient>().Respond(client => client.GetItemAsync(null), new Item { Name = "Book" });
        _shop.Service<Stock.StockClient>().Respond(client => client.GetStockAsync(null), new StockLevel { Quantity = 7 });

        var item = await new Catalog.CatalogClient(_shopChannel!).GetItemAsync(new GetItemRequest { Id = "1" });
        var level = await new Stock.StockClient(_shopChannel!).GetStockAsync(new GetStockRequest { ItemId = "1" });

        Assert.Equal("Book", item.Name);
        Assert.Equal(7, level.Quantity);
        Assert.Equal("1", Assert.Single(_shop.Service<Stock.StockClient>().Requests<GetStockRequest>()).ItemId);
        Assert.Same(_shop.Service<Stock.StockClient>(), _shop.Service<Stock.StockClient>());
        Assert.Equal("shop.Stock", _shop.Service<Stock.StockClient>().Descriptor.FullName);
    }

    [Fact]
    public async Task ShouldStubAgainAfterReset()
    {
        _catalog.Respond(client => client.GetItemAsync(null), new Item { Name = "Before" });
        await Client.GetItemAsync(new GetItemRequest { Id = "0" });
        _catalog.Reset();

        await Assert.ThrowsAsync<RpcException>(async () => await Client.GetItemAsync(new GetItemRequest { Id = "1" }));

        _catalog.Respond(client => client.GetItemAsync(null), new Item { Name = "After" });

        Assert.Equal("After", (await Client.GetItemAsync(new GetItemRequest { Id = "1" })).Name);
        Assert.Equal(["1", "1"], _catalog.Requests<GetItemRequest>().Select(request => request.Id).ToArray());
    }

    [Fact]
    public void ShouldRejectCallThatDoesNotNameOneMethod()
    {
        var none = Assert.Throws<ArgumentException>(() => _catalog.Respond(_ => null!, new Item()));

        Assert.Contains("GetItemAsync(null)", none.Message);
        Assert.Contains("made 0 calls", none.Message);

        var two = Assert.Throws<ArgumentException>(() => _catalog.Respond(client =>
        {
            client.FindItemAsync(null);
            return client.GetItemAsync(null);
        }, new Item()));

        Assert.Contains("made 2 calls", two.Message);
    }

    [Fact]
    public void ShouldRejectStreamingCall()
    {
        var exception = Assert.Throws<NotSupportedException>(() => _catalog.Respond(client =>
        {
            client.WatchItems(null);
            return client.GetItemAsync(null);
        }, new Item()));

        Assert.Contains("shop.Catalog/WatchItems", exception.Message);
    }

    [Fact]
    public void ShouldRejectPredicateOfAnotherRequestType()
    {
        var exception = Assert.Throws<ArgumentException>(() => _catalog.Respond(
            client => client.GetItemAsync(null), (DeleteItemRequest _) => true, new Item()));

        Assert.Contains("shop.Catalog/GetItem", exception.Message);
        Assert.Contains(nameof(DeleteItemRequest), exception.Message);
    }

    [Fact]
    public void ShouldRejectClientThatIsNotGenerated()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => _shop.Service<HandWrittenClient>());

        Assert.Contains(nameof(HandWrittenClient), exception.Message);
        Assert.Contains("Grpc.Tools", exception.Message);
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _component.StartAsync(default);
        _catalogChannel = GrpcChannel.ForAddress(_catalog.BaseAddress);
        _shopChannel = GrpcChannel.ForAddress(_shop.BaseAddress);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        _catalogChannel?.Dispose();
        _shopChannel?.Dispose();
        await _component.StopAsync(default);
    }
}

public class HandWrittenClient : ClientBase<HandWrittenClient>
{
    protected override HandWrittenClient NewInstance(ClientBaseConfiguration configuration) => new();
}
