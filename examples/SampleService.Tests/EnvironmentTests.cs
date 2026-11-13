using System.Net.Http.Json;
using DotNet.Testcontainers.Containers;
using Enhanced.Testing.Component;
using Enhanced.Testing.Component.DbContext;
using Enhanced.Testing.Component.GrpcClient;
using Enhanced.Testing.Component.PostgreSql;
using Enhanced.Testing.Component.Redis;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SampleService.Models;
using SampleService.Services;

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
            Assert.True(await DatabaseExistsAsync(firstDatabase.Database));
        }

        Assert.False(await DatabaseExistsAsync(firstDatabase.Database));
        Assert.False(await DatabaseExistsAsync(secondDatabase.Database));
        Assert.False(firstDatabase.IsStarted);
        Assert.True(fixture.PostgreSql.IsStarted);
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
