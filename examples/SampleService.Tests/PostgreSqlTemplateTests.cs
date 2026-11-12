using Enhanced.Testing.Component;
using Enhanced.Testing.Component.PostgreSql;
using Microsoft.EntityFrameworkCore;
using SampleService.Models;

namespace SampleService.Tests;

public sealed class PostgreSqlTemplateTests : IAsyncLifetime
{
    private const string Template = "people_template";

    private readonly PostgreSqlHarness _postgreSql = new();
    private readonly IComponent _component;

    public PostgreSqlTemplateTests()
    {
        _component = ComponentBuilder.Create<Program>()
                                     .AddHarness(_postgreSql)
                                     .Build();
    }

    [Fact]
    public async Task ShouldCreateIndependentDatabasesFromSeededTemplate()
    {
        await _postgreSql.CreateTemplateAsync(Template, SeedAsync);

        var first = await _postgreSql.CreateDatabaseAsync("people_first", Template);
        var second = await _postgreSql.CreateDatabaseAsync("people_second", Template);

        await using (var context = CreateContext(first))
        {
            context.Persons.Add(new Person { Name = "Bob" });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(first))
        {
            Assert.Equal(["Alice", "Bob"], await context.Persons.OrderBy(p => p.Id).Select(p => p.Name).ToListAsync());
        }

        await using (var context = CreateContext(second))
        {
            Assert.Equal(["Alice"], await context.Persons.Select(p => p.Name).ToListAsync());
        }
    }

    [Fact]
    public async Task ShouldRecreateTemplateAndDropDatabases()
    {
        await _postgreSql.CreateTemplateAsync(Template, _ => Task.CompletedTask);
        await _postgreSql.CreateTemplateAsync(Template, SeedAsync);

        var connectionString = await _postgreSql.CreateDatabaseAsync("people_dropped", Template);

        await using (var context = CreateContext(connectionString))
        {
            Assert.Equal(1, await context.Persons.CountAsync());
        }

        await _postgreSql.DropDatabaseAsync("people_dropped");
        await _postgreSql.DropDatabaseAsync(Template);
        await _postgreSql.DropDatabaseAsync("missing");

        var databases = await _postgreSql.Container.ExecAsync(
            ["psql", "-U", "postgres", "-tAc", "SELECT string_agg(datname, ',') FROM pg_database WHERE datname LIKE 'people%'"]);

        Assert.Equal(string.Empty, databases.Stdout.Trim());
    }

    [Fact]
    public async Task ShouldCloneTemplateWhenSeedLeavesPooledConnectionsOpen()
    {
        await _postgreSql.CreateTemplateAsync(
            Template,
            _ => SeedAsync(_postgreSql.GetConnectionString(Template)));

        var connectionString = await _postgreSql.CreateDatabaseAsync("people_clone", Template);

        await using var context = CreateContext(connectionString);
        Assert.Equal(1, await context.Persons.CountAsync());
    }

    [Fact]
    public async Task ShouldCreateEmptyDatabaseAndReportErrors()
    {
        var connectionString = await _postgreSql.CreateDatabaseAsync("empty_database");

        await using (var context = CreateContext(connectionString))
        {
            Assert.True(await context.Database.CanConnectAsync());
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _postgreSql.CreateDatabaseAsync("empty_database"));

        Assert.Contains("already exists", exception.Message);
    }

    public Task InitializeAsync() => _component.StartAsync(default);

    public Task DisposeAsync() => _component.StopAsync(default);

    private static async Task SeedAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);
        await context.Database.EnsureCreatedAsync();
        context.Persons.Add(new Person { Name = "Alice" });
        await context.SaveChangesAsync();
    }

    private static PeopleDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<PeopleDbContext>().UseNpgsql(connectionString).Options);
}
