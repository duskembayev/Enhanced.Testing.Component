using System.Net.Http.Json;
using DotNet.Testcontainers.Containers;
using Enhanced.Testing.Component;
using Enhanced.Testing.Component.DbContext;
using Enhanced.Testing.Component.GrpcClient;
using Enhanced.Testing.Component.PostgreSql;
using Enhanced.Testing.Component.Redis;
using Enhanced.Testing.Component.WireMock;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SampleService.Models;
using SampleService.Services;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace SampleService.Tests;

public class EnvironmentTests(SampleEnvironmentFixture fixture) : IClassFixture<SampleEnvironmentFixture>
{
    [Fact]
    public async Task ShouldWarmUpCacheForEveryComponent()
    {
        await fixture.SeedPeopleAsync();

        var emptyClient = new HttpClientHarness();
        await using var emptyComponent = await fixture.Environment
                                                      .CreateComponent<Program>()
                                                      .AddHarness(emptyClient)
                                                      .StartAsync();

        await fixture.SeedPeopleAsync("Alice", "Bob");

        var seededClient = new HttpClientHarness();
        await using var seededComponent = await fixture.Environment
                                                       .CreateComponent<Program>()
                                                       .AddHarness(seededClient)
                                                       .StartAsync();

        Assert.Empty(await GetCachedPeopleAsync(emptyClient));
        Assert.Equal(["Alice", "Bob"], await GetCachedPeopleAsync(seededClient));
    }

    [Fact]
    public async Task ShouldApplyConfigurationPerComponent()
    {
        await fixture.SeedPeopleAsync("Carol");

        var disabledClient = new HttpClientHarness();
        await using var disabledComponent = await fixture.Environment
                                                         .CreateComponent<Program>(builder =>
                                                             builder.UseSetting(PeopleCacheWarmup.EnabledSettingName,
                                                                 "false"))
                                                         .AddHarness(disabledClient)
                                                         .StartAsync();

        var enabledClient = new HttpClientHarness();
        await using var enabledComponent = await fixture.Environment
                                                        .CreateComponent<Program>()
                                                        .AddHarness(enabledClient)
                                                        .StartAsync();

        Assert.Empty(await GetCachedPeopleAsync(disabledClient));
        Assert.Equal(["Carol"], await GetCachedPeopleAsync(enabledClient));
    }

    [Fact]
    public async Task ShouldKeepEnvironmentHarnessesAcrossComponents()
    {
        await fixture.SeedPeopleAsync();

        IContainer[] containers = [fixture.PostgreSql.Container, fixture.Kafka.Container, fixture.Redis.Container];
        var containerIds = containers.Select(container => container.Id).ToArray();
        var grpcClient = new GrpcClientHarness();
        var name = Guid.NewGuid().ToString("N");

        for (var i = 1; i <= 2; i++)
        {
            var redisDatabase = new RedisDatabaseHarness(fixture.Redis);

            await using (var component = await fixture.Environment
                                                      .CreateComponent<Program>()
                                                      .AddHarness(grpcClient)
                                                      .AddHarness(redisDatabase)
                                                      .StartAsync())
            {
                var reply = await grpcClient.CreateClient<Greeter.GreeterClient>()
                                            .SayHelloAsync(new HelloRequest { Name = name });

                Assert.Equal("Hello " + name, reply.Message);
                Assert.Equal(i, (int)await redisDatabase.Database.StringGetAsync(name));
            }

            Assert.Equal(containerIds, containers.Select(container => container.Id));
            Assert.All(containers, container => Assert.Equal(TestcontainersStates.Running, container.State));
            Assert.Same(containers[0], fixture.PostgreSql.Container);
        }

        await using var dbContext = fixture.CreatePeopleDbContext();
        Assert.Equal(2, await dbContext.Persons.CountAsync(p => p.Name == name));
    }

    [Fact]
    public async Task ShouldRejectHarnessUsedByAnotherRunningComponent()
    {
        var httpClient = new HttpClientHarness();

        await using var first = await fixture.Environment
                                             .CreateComponent<Program>()
                                             .AddHarness(httpClient)
                                             .StartAsync();

        var second = fixture.Environment
                            .CreateComponent<Program>()
                            .AddHarness(httpClient);

        await Assert.ThrowsAsync<NotSupportedException>(() => second.StartAsync());

        var response = await httpClient.CreateClient().GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ShouldNotStopEnvironmentHarnessAddedToComponent()
    {
        var component = fixture.Environment
                               .CreateComponent<Program>()
                               .AddHarness(fixture.Redis);

        await Assert.ThrowsAsync<NotSupportedException>(() => component.StartAsync());

        Assert.Equal(TestcontainersStates.Running, fixture.Redis.Container.State);
    }

    [Fact]
    public async Task ShouldGiveEveryComponentItsOwnDatabaseFromTemplate()
    {
        var template = $"people_{Guid.NewGuid():N}";

        await fixture.PostgreSql.CreateTemplateAsync(template, async connectionString =>
        {
            await using var dbContext = new PeopleDbContext(
                new DbContextOptionsBuilder<PeopleDbContext>().UseNpgsql(connectionString).Options);

            await dbContext.Database.EnsureCreatedAsync();
            dbContext.Persons.Add(new Person { Name = "Alice" });
            await dbContext.SaveChangesAsync();
        });

        var firstDatabase = CreateDatabaseHarness(template);
        var firstDbContext = new DbContextHarness<PeopleDbContext>();
        var firstClient = new HttpClientHarness();
        var secondDatabase = CreateDatabaseHarness(template);
        var secondClient = new HttpClientHarness();

        await using (var first = await fixture.Environment
                                              .CreateComponent<Program>()
                                              .AddHarness(firstDatabase)
                                              .AddHarness(firstDbContext)
                                              .AddHarness(firstClient)
                                              .StartAsync())
        {
            await firstDbContext.ExecuteAsync(async (dbContext, cancellationToken) =>
            {
                dbContext.Persons.Add(new Person { Name = "Bob" });
                await dbContext.SaveChangesAsync(cancellationToken);
            });

            await using var second = await fixture.Environment
                                                  .CreateComponent<Program>()
                                                  .AddHarness(secondDatabase)
                                                  .AddHarness(secondClient)
                                                  .StartAsync();

            Assert.Equal(["Alice"], await GetCachedPeopleAsync(firstClient));
            Assert.Equal(["Alice"], await GetCachedPeopleAsync(secondClient));
            Assert.Equal(2, await firstDbContext.ExecuteAsync((dbContext, cancellationToken) =>
                dbContext.Persons.CountAsync(cancellationToken)));
            Assert.True(await DatabaseExistsAsync(firstDatabase.CurrentDatabase!));
        }

        Assert.False(await DatabaseExistsAsync(firstDatabase.CurrentDatabase!));
        Assert.False(await DatabaseExistsAsync(secondDatabase.CurrentDatabase!));
        Assert.False(firstDatabase.IsStarted);
        Assert.True(fixture.PostgreSql.IsStarted);
    }

    [Fact]
    public async Task ShouldGiveReusedDatabaseHarnessNewDatabaseOnEveryStart()
    {
        var database = new PostgreSqlDatabaseHarness(fixture.PostgreSql)
        {
            ConnectionName = PeopleDbContext.ConnectionStringName
        };
        var dbContext = new DbContextHarness<PeopleDbContext>();
        dbContext.EnsureCreatedOnStart(builder => builder.UseNpgsql(database.GetConnectionString()));
        var names = new List<string>();

        for (var i = 0; i < 3; i++)
        {
            var http = new HttpClientHarness();

            await using (await fixture.Environment
                                      .CreateComponent<Program>()
                                      .AddHarness(database)
                                      .AddHarness(dbContext)
                                      .AddHarness(http)
                                      .StartAsync())
            {
                Assert.Empty(await GetCachedPeopleAsync(http));
                names.Add(database.CurrentDatabase!);
            }
        }

        Assert.Equal(3, names.Distinct().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShouldWaitForStartingComponentWhenStopped(bool blockSynchronously)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var environment = await ComponentEnvironment.Create().StartAsync();
        var http = new HttpClientHarness();
        var builder = environment.CreateComponent<Program>()
                                 .AddDependency(new BlockingDependency(entered, release, blockSynchronously))
                                 .AddHarness(http);

        var start = Task.Run(() => builder.StartAsync());

        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));
        var stop = environment.StopAsync();

        await Task.Delay(200);
        Assert.False(stop.IsCompleted);
        release.Set();

        var component = await start;
        await stop;

        Assert.Throws<InvalidOperationException>(() => component.Services);
        Assert.False(http.IsStarted);
    }

    [Fact]
    public async Task ShouldCancelStopThatWaitsForStartingComponent()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var environment = await ComponentEnvironment.Create().StartAsync();
        var builder = environment.CreateComponent<Program>()
                                 .AddDependency(new BlockingDependency(entered, release, true));

        var start = Task.Run(() => builder.StartAsync());
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAsync<OperationCanceledException>(() => environment.StopAsync(cts.Token));

        release.Set();
        await start;
        await environment.StopAsync();
    }

    [Fact]
    public async Task ShouldNotDropDatabaseThatAlreadyExisted()
    {
        var existing = $"existing_{Guid.NewGuid():N}";
        await fixture.PostgreSql.CreateDatabaseAsync(existing);

        var database = new PostgreSqlDatabaseHarness(fixture.PostgreSql) { Database = existing };
        var builder = fixture.Environment.CreateComponent<Program>().AddHarness(database);

        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.StartAsync());

        Assert.True(await DatabaseExistsAsync(existing));
        Assert.False(database.IsStarted);

        await fixture.PostgreSql.DropDatabaseAsync(existing);
    }

    [Fact]
    public async Task ShouldRollBackCleanlyWhenDatabaseNameIsInvalid()
    {
        var database = new PostgreSqlDatabaseHarness(fixture.PostgreSql) { Database = new string('a', 64) };
        var builder = fixture.Environment.CreateComponent<Program>().AddHarness(database);

        await Assert.ThrowsAsync<ArgumentException>(() => builder.StartAsync());

        Assert.False(database.IsStarted);
    }

    [Fact]
    public async Task ShouldShareWireMockServerBetweenComponents()
    {
        var quotesApi = new WireMockHarness { ConfigurationKey = "QuotesApi:BaseUrl" };

        await using var environment = await ComponentEnvironment.Create().AddHarness(quotesApi).StartAsync();

        quotesApi.Server
                 .Given(Request.Create().WithPath("/quote").UsingGet())
                 .RespondWith(Response.Create().WithBody("shared"));

        for (var i = 0; i < 2; i++)
        {
            var http = new HttpClientHarness();
            await using var component = await environment.CreateComponent<Program>().AddHarness(http).StartAsync();

            Assert.Equal("shared", await http.CreateClient().GetStringAsync("/quote"));
        }

        Assert.Equal(2, quotesApi.Server.LogEntries.Count());
    }

    [Fact]
    public async Task ShouldCreateGrpcClientAfterFailedAttemptDuringStart()
    {
        var grpcClient = new GrpcClientHarness();
        Exception? early = null;

        await using var component = await fixture.Environment
                                                 .CreateComponent<Program>()
                                                 .AddHarness(grpcClient)
                                                 .AddDependency(new StartProbe(() =>
                                                     early = Record.Exception(() =>
                                                         grpcClient.CreateClient<Greeter.GreeterClient>())))
                                                 .StartAsync();

        Assert.IsType<InvalidOperationException>(early);

        var reply = await grpcClient.CreateClient<Greeter.GreeterClient>()
                                    .SayHelloAsync(new HelloRequest { Name = "probe" });

        Assert.Equal("Hello probe", reply.Message);
    }

    [Fact]
    public async Task ShouldApplyEnvironmentConfigurationInCallOrder()
    {
        await using var environment = await ComponentEnvironment.Create()
                                                                .AddHarness(new RedisHarness { ConnectionName = "Ordered" })
                                                                .Configure(builder =>
                                                                    builder.UseSetting("ConnectionStrings:Ordered", "configured"))
                                                                .StartAsync();

        await using var component = await environment.CreateComponent<Program>().StartAsync();

        var configuration = component.Services.GetRequiredService<IConfiguration>();
        Assert.Equal("configured", configuration.GetConnectionString("Ordered"));
    }

    [Fact]
    public async Task ShouldAllowConcurrentStops()
    {
        var environment = await ComponentEnvironment.Create().AddHarness(new RedisHarness()).StartAsync();

        await Task.WhenAll(environment.StopAsync(), environment.DisposeAsync().AsTask());
    }

    private sealed class BlockingDependency(
        ManualResetEventSlim entered,
        ManualResetEventSlim release,
        bool blockSynchronously) : IComponentDependency
    {
        public async Task OnStartAsync(IComponent component, CancellationToken cancellationToken)
        {
            if (!blockSynchronously)
            {
                await Task.Yield();
            }

            entered.Set();
            release.Wait(TimeSpan.FromSeconds(30));
        }

        public Task OnStopAsync(IComponent component, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StartProbe(Action onStart) : IComponentDependency
    {
        public Task OnStartAsync(IComponent component, CancellationToken cancellationToken)
        {
            onStart();
            return Task.CompletedTask;
        }

        public Task OnStopAsync(IComponent component, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private PostgreSqlDatabaseHarness CreateDatabaseHarness(string template) =>
        new(fixture.PostgreSql) { ConnectionName = PeopleDbContext.ConnectionStringName, Template = template };

    private async Task<bool> DatabaseExistsAsync(string database)
    {
        await using var connection = new NpgsqlConnection(fixture.PostgreSql.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = @name", connection);
        command.Parameters.AddWithValue("name", database);
        return (long)(await command.ExecuteScalarAsync())! > 0;
    }

    private static async Task<string[]> GetCachedPeopleAsync(HttpClientHarness httpClient) =>
        await httpClient.CreateClient().GetFromJsonAsync<string[]>("/people/cached") ?? [];
}
