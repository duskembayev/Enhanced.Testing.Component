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
    Image = KafkaHarness.ConfluentImage, Topics = [Topic], ConnectionName = ConnectionName
});

public sealed class NativeKafkaFixture() : KafkaBrokerFixture(new KafkaHarness
{
    Topics = [Topic], ConnectionName = ConnectionName
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

        var message = await fixture.Consumer.WaitForKeyAsync(key, MessageTimeout);

        Assert.Equal("value", message.Value);
    }

    [Fact]
    public async Task ShouldSkipMessagesThatDoNotMatch()
    {
        var expected = Guid.NewGuid().ToString("N");

        await fixture.Producer.ProduceAsync(Guid.NewGuid().ToString("N"), "other");
        await fixture.Producer.ProduceAsync(expected, "expected");

        var result = await fixture.Consumer.WaitForMessageAsync(r => r.Message.Key == expected, MessageTimeout);

        Assert.Equal("expected", result.Message.Value);
    }

    [Fact]
    public async Task ShouldTimeOutWhenNoMessageMatches()
    {
        await fixture.Producer.ProduceAsync(Guid.NewGuid().ToString("N"), "other");

        var exception = await Assert.ThrowsAsync<TimeoutException>(()
            => fixture.Consumer.WaitForKeyAsync("missing", TimeSpan.FromSeconds(1)));

        Assert.Contains("skipped", exception.Message);
    }

    [Fact]
    public async Task ShouldFindEarlierMessageAfterSeekToBeginning()
    {
        var key = Guid.NewGuid().ToString("N");

        await fixture.Producer.ProduceAsync(key, "earlier");
        await fixture.Consumer.WaitForKeyAsync(key, MessageTimeout);

        fixture.Consumer.SeekToBeginning();

        var message = await fixture.Consumer.WaitForKeyAsync(key, MessageTimeout);

        Assert.Equal("earlier", message.Value);
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
