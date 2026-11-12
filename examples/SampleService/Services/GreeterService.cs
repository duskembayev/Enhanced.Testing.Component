using Confluent.Kafka;
using Grpc.Core;
using SampleService.Models;
using StackExchange.Redis;

namespace SampleService.Services;

public class GreeterService(
    PeopleDbContext dbContext,
    IProducer<string, string> producer,
    IConnectionMultiplexer redisConnection) : Greeter.GreeterBase
{
    public const string KafkaTopic = "sample-topic";
    public const string KafkaConnectionStringName = "Kafka";
    public const string RedisConnectionStringName = "Redis";

    public override async Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
    {
        await dbContext.Persons.AddAsync(new Person { Name = request.Name }, context.CancellationToken);
        await dbContext.SaveChangesAsync(context.CancellationToken);
        await producer.ProduceAsync(KafkaTopic, new Message<string, string> { Key = request.Name, Value = request.Name }, context.CancellationToken);
        await redisConnection.GetDatabase().StringIncrementAsync(request.Name);

        return new HelloReply { Message = "Hello " + request.Name };
    }
}
