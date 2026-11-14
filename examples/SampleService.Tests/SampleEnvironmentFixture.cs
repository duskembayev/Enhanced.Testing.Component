using Enhanced.Testing.Component;
using Enhanced.Testing.Component.Kafka;
using Enhanced.Testing.Component.PostgreSql;
using Enhanced.Testing.Component.Redis;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using SampleService.Models;
using SampleService.Services;

namespace SampleService.Tests;

public class SampleEnvironmentFixture : IAsyncLifetime
{
    private IComponentEnvironment? _environment;

    public PostgreSqlHarness PostgreSql { get; } = new() { ConnectionName = PeopleDbContext.ConnectionStringName };

    public KafkaHarness Kafka { get; } = new()
    {
        Topics = [GreeterService.KafkaTopic], ConnectionName = GreeterService.KafkaConnectionStringName
    };

    public RedisHarness Redis { get; } = new() { ConnectionName = GreeterService.RedisConnectionStringName };

    public IComponentEnvironment Environment =>
        _environment ?? throw new InvalidOperationException("The environment is not started.");

    async Task IAsyncLifetime.InitializeAsync()
    {
        _environment = await ComponentEnvironment.Create()
                                                 .AddHarness(PostgreSql)
                                                 .AddHarness(Kafka)
                                                 .AddHarness(Redis)
                                                 .Configure(builder =>
                                                     builder.UseSetting(PeopleCacheWarmup.EnabledSettingName, "true"))
                                                 .StartAsync();

        await using var dbContext = CreatePeopleDbContext();
        await dbContext.Database.EnsureCreatedAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_environment is not null)
        {
            await _environment.DisposeAsync();
        }
    }

    public PeopleDbContext CreatePeopleDbContext() =>
        new(new DbContextOptionsBuilder<PeopleDbContext>().UseNpgsql(PostgreSql.GetConnectionString()).Options);

    public async Task SeedPeopleAsync(params string[] names)
    {
        await using var dbContext = CreatePeopleDbContext();
        await dbContext.Persons.ExecuteDeleteAsync();
        dbContext.Persons.AddRange(names.Select(name => new Person { Name = name }));
        await dbContext.SaveChangesAsync();
    }
}
