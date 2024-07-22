using Enhanced.Testing.Component;
using Enhanced.Testing.Component.DbContext;
using Enhanced.Testing.Component.GrpcClient;
using Enhanced.Testing.Component.Kafka;
using Enhanced.Testing.Component.PostgreSql;
using Enhanced.Testing.Component.Redis;
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
            Topics = [GreeterService.KafkaTopic], ConnectionName = GreeterService.KafkaConnectionStringName
        };
        Redis = new RedisStackHarness { ConnectionName = GreeterService.RedisConnectionStringName };
        PostgreSql = new PostgreSqlHarness() { ConnectionName = PeopleDbContext.ConnectionStringName, };
        PeopleDb = new DbContextHarness<PeopleDbContext> { EnsureCreated = true };
        PeopleKafkaConsumer = new KafkaConsumerHarness<string, string>(Kafka) { Topic = GreeterService.KafkaTopic };
        RedisDatabase = new RedisDatabaseHarness(Redis);

        _component = ComponentBuilder.Create<Program>()
                                     .AddHarness(HttpClient)
                                     .AddHarness(GrpcClient)
                                     .AddHarness(PostgreSql)
                                     .AddHarness(Kafka)
                                     .AddHarness(Redis)
                                     .AddHarness(PeopleDb)
                                     .AddHarness(PeopleKafkaConsumer)
                                     .AddHarness(RedisDatabase)
                                     .Build();
    }

    public HttpClientHarness HttpClient { get; }

    public GrpcClientHarness GrpcClient { get; }

    public PostgreSqlHarness PostgreSql { get; }

    public KafkaHarness Kafka { get; }

    public RedisStackHarness Redis { get; }

    public DbContextHarness<PeopleDbContext> PeopleDb { get; }

    public KafkaConsumerHarness<string, string> PeopleKafkaConsumer { get; }

    public RedisDatabaseHarness RedisDatabase { get; }

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _component.StartAsync(default);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _component.StopAsync(default);
    }
}
