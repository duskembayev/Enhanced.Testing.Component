using Confluent.Kafka;
using Grpc.Core;
using SampleService.Models;
using StackExchange.Redis;

namespace SampleService.Services;

public class GreeterService : Greeter.GreeterBase, IDisposable
{
    public const string KafkaTopic = "sample-topic";
    public const string KafkaConnectionStringName = "Kafka";
    public const string RedisConnectionStringName = "Redis";

    private readonly PeopleDbContext _dbContext;
    private readonly IProducer<string, string> _producer;
    private readonly ConnectionMultiplexer _redisConnection;

    public GreeterService(PeopleDbContext dbContext, IConfiguration configuration)
    {
        _dbContext = dbContext;

        _producer = new ProducerBuilder<string, string>(new ProducerConfig()
        {
            BootstrapServers = configuration.GetConnectionString(KafkaConnectionStringName)
        }).Build();

        _redisConnection = ConnectionMultiplexer.Connect(configuration.GetConnectionString(RedisConnectionStringName)!);
    }

    public override async Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
    {
        await _dbContext.Persons.AddAsync(new Person { Name = request.Name }, context.CancellationToken);
        await _dbContext.SaveChangesAsync(context.CancellationToken);
        await _producer.ProduceAsync(KafkaTopic, new Message<string, string> { Key = request.Name, Value = request.Name }, context.CancellationToken);
        await _redisConnection.GetDatabase().StringIncrementAsync(request.Name);

        return new HelloReply { Message = "Hello " + request.Name };
    }

    public void Dispose()
    {
        _producer.Dispose();
    }
}
