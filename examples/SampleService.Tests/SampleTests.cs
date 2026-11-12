using Enhanced.Testing.Component.Kafka;
using Microsoft.EntityFrameworkCore;

namespace SampleService.Tests;

public class SampleTests(SampleServiceFixture fixture) : IClassFixture<SampleServiceFixture>
{
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);

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
        var client = fixture.GrpcClient.CreateClient<Greeter.GreeterClient>();

        await client.SayHelloAsync(new HelloRequest { Name = "Alice" });

        var message = await fixture.PeopleKafkaConsumer.WaitForKeyAsync("Alice", MessageTimeout);

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

    [Fact]
    public async Task ShouldProduceMessageWithHarness()
    {
        await fixture.PeopleKafkaProducer.ProduceAsync("Bob", "Hello Bob");

        var message = await fixture.PeopleKafkaConsumer.WaitForKeyAsync("Bob", MessageTimeout);

        Assert.Equal("Hello Bob", message.Value);
    }
}
