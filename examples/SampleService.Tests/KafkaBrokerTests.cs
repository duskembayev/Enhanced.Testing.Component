using Enhanced.Testing.Component;
using Enhanced.Testing.Component.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SampleService.Tests;

public abstract class KafkaBrokerFixture : IAsyncLifetime
{
    public const string Topic = "broker-tests";
    public const string ConnectionName = "Kafka";

    private readonly IComponent _component;

    protected KafkaBrokerFixture(IKafkaHarness broker)
    {
        Broker = broker;
        Consumer = new KafkaConsumerHarness<string, string>(broker) { Topic = Topic };
        Producer = new KafkaProducerHarness<string, string>(broker) { Topic = Topic };

        _component = ComponentBuilder.Create<Program>()
                                     .AddHarness(broker)
                                     .AddHarness(Consumer)
                                     .AddHarness(Producer)
                                     .Build();
    }

    public IKafkaHarness Broker { get; }

    public KafkaConsumerHarness<string, string> Consumer { get; }

    public KafkaProducerHarness<string, string> Producer { get; }

    public IServiceProvider Services => _component.Services;

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _component.StartAsync(default);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _component.StopAsync(default);
    }
}

public sealed class ConfluentKafkaFixture() : KafkaBrokerFixture(new KafkaHarness
{
    Topics = [Topic], ConnectionName = ConnectionName
});

public sealed class NativeKafkaFixture() : KafkaBrokerFixture(new KafkaHarness
{
    Image = KafkaHarness.NativeImage, Topics = [Topic], ConnectionName = ConnectionName
});

public sealed class RedpandaFixture() : KafkaBrokerFixture(new RedpandaHarness
{
    Topics = [Topic], ConnectionName = ConnectionName
});

public abstract class KafkaBrokerTests<TFixture>(TFixture fixture) : IClassFixture<TFixture>
    where TFixture : KafkaBrokerFixture
{
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ShouldProduceAndConsume()
    {
        var key = Guid.NewGuid().ToString("N");

        await fixture.Producer.ProduceAsync(key, "value");

        var message = ConsumeKey(key);

        Assert.Equal("value", message.Value);
    }

    private Confluent.Kafka.Message<string, string> ConsumeKey(string key)
    {
        var deadline = DateTime.UtcNow + MessageTimeout;

        while (DateTime.UtcNow < deadline)
        {
            var message = fixture.Consumer.ConsumeMessage(deadline - DateTime.UtcNow);

            if (message?.Key == key)
            {
                return message;
            }
        }

        throw new TimeoutException($"Message '{key}' was not received.");
    }

    [Fact]
    public async Task ShouldCreateTopic()
    {
        var topic = "topic-" + Guid.NewGuid().ToString("N");

        Assert.True(await fixture.Broker.CreateTopicAsync(topic));
        Assert.False(await fixture.Broker.CreateTopicAsync(topic));
        Assert.False(await fixture.Broker.CreateTopicAsync("invalid topic name"));
    }

    [Fact]
    public void ShouldPassConnectionStringToApplication()
    {
        var configuration = fixture.Services.GetRequiredService<IConfiguration>();

        Assert.Equal(fixture.Broker.GetConnectionString(), configuration.GetConnectionString(KafkaBrokerFixture.ConnectionName));
    }
}

public class ConfluentKafkaTests(ConfluentKafkaFixture fixture) : KafkaBrokerTests<ConfluentKafkaFixture>(fixture);

public class NativeKafkaTests(NativeKafkaFixture fixture) : KafkaBrokerTests<NativeKafkaFixture>(fixture);

public class RedpandaTests(RedpandaFixture fixture) : KafkaBrokerTests<RedpandaFixture>(fixture);
