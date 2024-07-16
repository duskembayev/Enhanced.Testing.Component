using Enhanced.Testing.Component;
using Enhanced.Testing.Component.DbContext;
using Enhanced.Testing.Component.GrpcClient;
using Enhanced.Testing.Component.Kafka;
using Enhanced.Testing.Component.PostgreSql;
using SampleService.Models;
using SampleService.Services;

namespace SampleService.Tests;

public class SampleServiceFixture : IAsyncLifetime
{
    private readonly IComponent _component;

    public SampleServiceFixture()
    {
        HttpClient = new HttpClientHarness();
        GrpcClient = new GrpcClientHarness();
        Kafka = new KafkaHarness
        {
            Topics = [GreeterService.KafkaTopic],
            ConnectionStringName = GreeterService.KafkaConnectionStringName
        };
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
                                     .AddHarness(Kafka)
                                     .AddHarness(PeopleDb)
                                     .Build();
    }

    public HttpClientHarness HttpClient { get; }

    public GrpcClientHarness GrpcClient { get; }

    public PostgreSqlHarness PostgreSql { get; }

    public KafkaHarness Kafka { get; }

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
