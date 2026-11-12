using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using SampleService.Models;
using SampleService.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddDbContext<PeopleDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString(PeopleDbContext.ConnectionStringName);
    options.UseNpgsql(connectionString);
});

builder.Services.AddSingleton<IProducer<string, string>>(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var config = new ProducerConfig
    {
        BootstrapServers = configuration.GetConnectionString(GreeterService.KafkaConnectionStringName)
    };

    return new ProducerBuilder<string, string>(config).Build();
});

builder.Services.AddSingleton<IConnectionMultiplexer>(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    return ConnectionMultiplexer.Connect(configuration.GetConnectionString(GreeterService.RedisConnectionStringName)!);
});

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapGet("/time", (TimeProvider timeProvider) => timeProvider.GetUtcNow());
app.MapGet("/delay/{seconds:int}",
    async (int seconds, TimeProvider timeProvider, CancellationToken cancellationToken) =>
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds), timeProvider, cancellationToken);
        return timeProvider.GetUtcNow();
    });
app.MapGrpcService<GreeterService>();
app.MapGet("/",
    () =>
        "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
