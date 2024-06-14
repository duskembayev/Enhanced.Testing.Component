using Enhanced.Testing.Component;
using Enhanced.Testing.Component.DbContext;
using Enhanced.Testing.Component.GrpcClient;
using Enhanced.Testing.Component.PostgreSql;
using SampleService.Models;

namespace SampleService.Tests;

public class SampleServiceFixture : IAsyncLifetime
{
    private readonly IComponent _component;

    public SampleServiceFixture()
    {
        HttpClient = new HttpClientHarness();
        GrpcClient = new GrpcClientHarness();
        PostgreSql = new PostgreSqlHarness()
        {
            ConnectionStringName = PeopleDbContext.ConnectionStringName,
        };
        PeopleDb = new DbContextHarness<PeopleDbContext>
        {
            EnsureCreated = true
        };

        _component = ComponentBuilder.Create<Program>()
                                     .AddHarness(HttpClient)
                                     .AddHarness(GrpcClient)
                                     .AddHarness(PostgreSql)
                                     .AddHarness(PeopleDb)
                                     .Build();
    }

    public HttpClientHarness HttpClient { get; }

    public GrpcClientHarness GrpcClient { get; }

    public PostgreSqlHarness PostgreSql { get; }

    public DbContextHarness<PeopleDbContext> PeopleDb { get; }

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _component.StartAsync(default);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _component.StopAsync(default);
    }
}
