using Enhanced.Testing.Component.Kafka;
using Microsoft.EntityFrameworkCore;

namespace SampleService.Tests;

public class SampleTests(SampleServiceFixture fixture) : IClassFixture<SampleServiceFixture>
{
    [Fact]
    public async Task ShouldBeHealthy()
    {
        var httpClient = fixture.HttpClient.CreateClient();

        var response = await httpClient.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadAsStringAsync();

        Assert.Equal("Healthy", status);
    }

    [Fact]
    public async Task ShouldSayHello()
    {
        var client = fixture.GrpcClient.CreateClient<Greeter.GreeterClient>();

        var reply = await client.SayHelloAsync(new HelloRequest { Name = "John" });

        Assert.Equal("Hello John", reply.Message);
    }

    [Fact]
    public async Task ShouldStorePerson()
    {
        var client = fixture.GrpcClient.CreateClient<Greeter.GreeterClient>();

        await client.SayHelloAsync(new HelloRequest { Name = "Kelly" });

        var exists = await fixture.PeopleDb.ExecuteAsync(async dbContext
            => await dbContext.Persons.AnyAsync(p => p.Name == "Kelly"));

        Assert.True(exists);
    }

    [Fact]
    public async Task ShouldProduceMessage()
    {
        fixture.PeopleKafkaConsumer.SeekToEnd();

        var client = fixture.GrpcClient.CreateClient<Greeter.GreeterClient>();

        await client.SayHelloAsync(new HelloRequest { Name = "Alice" });

        var message = fixture.PeopleKafkaConsumer.ConsumeMessage(500);

        Assert.NotNull(message);
        Assert.Equal("Alice", message.Key);
        Assert.Equal("Alice", message.Value);
    }

    [Fact]
    public async Task ShouldStorePersonInRedis()
    {
        var client = fixture.GrpcClient.CreateClient<Greeter.GreeterClient>();

        await client.SayHelloAsync(new HelloRequest { Name = "Leo" });
        await client.SayHelloAsync(new HelloRequest { Name = "Leo" });
        await client.SayHelloAsync(new HelloRequest { Name = "Leo" });

        var value = fixture.RedisDatabase.Database.StringGet("Leo");

        Assert.Equal(3, value);
    }
}
