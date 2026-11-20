using Confluent.Kafka;
using Enhanced.Testing.Component;
using Enhanced.Testing.Component.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Enhanced.Testing.Component.Tests;

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
    public async Task ShouldIsolateConcurrentTestsWithUniqueTopics()
    {
        var results = await Task.WhenAll(RunIsolatedScenarioAsync("first"), RunIsolatedScenarioAsync("second"));

        Assert.Equal(["first", "second"], results);
    }

    [Fact]
    public async Task ShouldFindEarlierMessageInUniqueTopicAfterSeekToBeginning()
    {
        var topic = await fixture.Broker.CreateUniqueTopicAsync("seek");

        using var producer = await fixture.Broker.CreateProducerAsync<string, string>(topic);
        await producer.ProduceAsync("key", "earlier");

        using var consumer = await fixture.Broker.CreateConsumerAsync<string, string>(topic);
        consumer.SeekToBeginning();

        var message = await consumer.WaitForKeyAsync("key", MessageTimeout);

        Assert.Equal("earlier", message.Value);
    }

    [Fact]
    public async Task ShouldCreateUniqueTopicsWithPrefix()
    {
        var first = await fixture.Broker.CreateUniqueTopicAsync("orders");
        var second = await fixture.Broker.CreateUniqueTopicAsync("orders");

        Assert.StartsWith("orders-", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task ShouldNotSeeSharedTopicMessagesInUniqueTopic()
    {
        var topic = await fixture.Broker.CreateUniqueTopicAsync("isolated");

        using var consumer = await fixture.Broker.CreateConsumerAsync<string, string>(topic);
        using var producer = await fixture.Broker.CreateProducerAsync<string, string>(topic);

        await fixture.Producer.ProduceAsync("shared", "shared");
        await producer.ProduceAsync("unique", "unique");

        var result = await consumer.WaitForMessageAsync(_ => true, MessageTimeout);

        Assert.Equal("unique", result.Message.Key);
    }

    [Fact]
    public async Task ShouldRejectInvalidUniqueTopicPrefix()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Broker.CreateUniqueTopicAsync("invalid prefix"));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Broker.CreateUniqueTopicAsync(new string('a', 217)));

        var topic = await fixture.Broker.CreateUniqueTopicAsync(new string('a', 216));

        Assert.Equal(249, topic.Length);
    }

    [Fact]
    public async Task ShouldThrowWhenUniqueTopicClientsAreDisposed()
    {
        var topic = await fixture.Broker.CreateUniqueTopicAsync("disposed");

        var consumer = await fixture.Broker.CreateConsumerAsync<string, string>(topic);
        var producer = await fixture.Broker.CreateProducerAsync<string, string>(topic);

        consumer.Dispose();
        consumer.Dispose();
        producer.Dispose();
        producer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => consumer.Consume(TimeSpan.Zero));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => consumer.WaitForKeyAsync("key", MessageTimeout));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => producer.ProduceAsync("key", "value"));
    }

    [Fact]
    public async Task ShouldConsumeEveryPartition()
    {
        var topic = await fixture.Broker.CreateUniqueTopicAsync("partitions", 3);

        using var producer = CreatePartitionProducer();
        await ProduceAsync(producer, topic, 1, "earlier");

        using var consumer = await fixture.Broker.CreateConsumerAsync<string, string>(topic);

        await ProduceAsync(producer, topic, 2, "later");

        var later = await consumer.WaitForMessageAsync(result => result.Message.Key == "later", MessageTimeout);
        Assert.Equal(2, later.Partition.Value);

        consumer.SeekToBeginning();

        var earlier = await consumer.WaitForMessageAsync(result => result.Message.Key == "earlier", MessageTimeout);
        Assert.Equal(1, earlier.Partition.Value);
    }

    [Fact]
    public async Task ShouldSeekOnePartitionAndKeepTheOthers()
    {
        var topic = await fixture.Broker.CreateUniqueTopicAsync("seek-partition", 2);

        using var producer = CreatePartitionProducer();
        await ProduceAsync(producer, topic, 1, "before-consumer");

        using var consumer = await fixture.Broker.CreateConsumerAsync<string, string>(topic);

        var first = await ProduceAsync(producer, topic, 0, "first");
        await ProduceAsync(producer, topic, 0, "second");
        await ProduceAsync(producer, topic, 1, "other");

        await ReadUntilAsync(consumer, "second", "other");

        consumer.Seek(first.TopicPartitionOffset);
        await ProduceAsync(producer, topic, 1, "marker");

        Assert.Equal(["first", "marker", "second"], await ReadUntilAsync(consumer, "second", "marker"));

        consumer.SeekToBeginning(new TopicPartition(topic, 1));
        await ProduceAsync(producer, topic, 0, "third");

        Assert.Equal(["before-consumer", "marker", "other", "third"],
            await ReadUntilAsync(consumer, "marker", "third"));

        await ProduceAsync(producer, topic, 0, "skipped");
        consumer.SeekToEnd(new TopicPartition(topic, 0));
        await ProduceAsync(producer, topic, 0, "last");

        Assert.Equal(["last"], await ReadUntilAsync(consumer, "last"));
    }

    [Fact]
    public async Task ShouldKeepPositionOfPartitionThatWasNotRead()
    {
        var topic = await fixture.Broker.CreateUniqueTopicAsync("seek-unread", 2);

        using var producer = CreatePartitionProducer();
        await ProduceAsync(producer, topic, 1, "before-consumer");

        using var consumer = await fixture.Broker.CreateConsumerAsync<string, string>(topic);

        consumer.SeekToBeginning(new TopicPartition(topic, 0));
        await ProduceAsync(producer, topic, 0, "first");
        await ProduceAsync(producer, topic, 1, "marker");

        Assert.Equal(["first", "marker"], await ReadUntilAsync(consumer, "first", "marker"));
    }

    [Fact]
    public async Task ShouldSeekHarnessConsumerToConsumedMessage()
    {
        var key = Guid.NewGuid().ToString("N");

        await fixture.Producer.ProduceAsync(key, "replayed");
        var consumed = await fixture.Consumer.WaitForMessageAsync(result => result.Message.Key == key, MessageTimeout);

        fixture.Consumer.Seek(consumed.TopicPartitionOffset);

        var replayed = await fixture.Consumer.WaitForMessageAsync(_ => true, MessageTimeout);
        Assert.Equal(key, replayed.Message.Key);

        fixture.Consumer.SeekToBeginning(consumed.TopicPartition);
        Assert.Equal("replayed", (await fixture.Consumer.WaitForKeyAsync(key, MessageTimeout)).Value);

        fixture.Consumer.SeekToEnd(consumed.TopicPartition);
        Assert.Null(fixture.Consumer.Consume(TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task ShouldRejectSeekOutsideTheTopic()
    {
        var topic = await fixture.Broker.CreateUniqueTopicAsync("seek-invalid");

        using var consumer = await fixture.Broker.CreateConsumerAsync<string, string>(topic);

        Assert.Throws<ArgumentException>(() => consumer.Seek(new TopicPartitionOffset("another-topic", 0, 0)));
        Assert.Throws<ArgumentException>(() => consumer.Seek(new TopicPartitionOffset(topic, 1, 0)));
        Assert.Throws<ArgumentException>(() => consumer.Seek(new TopicPartitionOffset(topic, 0, Offset.Stored)));
        Assert.Throws<ArgumentException>(() => consumer.SeekToEnd(new TopicPartition(topic, 1)));
        Assert.Throws<ArgumentException>(() =>
            fixture.Consumer.SeekToBeginning(new TopicPartition(KafkaBrokerFixture.Topic, 1)));
    }

    [Fact]
    public async Task ShouldCreateTopicWithPartitions()
    {
        var topic = "topic-" + Guid.NewGuid().ToString("N");

        Assert.True(await fixture.Broker.CreateTopicAsync(topic, 3));
        Assert.False(await fixture.Broker.CreateTopicAsync(topic, 3));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Broker.CreateTopicAsync(topic, 0));

        using var producer = CreatePartitionProducer();

        Assert.Equal(2, (await ProduceAsync(producer, topic, 2, "key")).Partition.Value);
    }

    private IProducer<string, string> CreatePartitionProducer() =>
        new ProducerBuilder<string, string>(
            new ProducerConfig { BootstrapServers = fixture.Broker.GetConnectionString() }).Build();

    private static Task<DeliveryResult<string, string>> ProduceAsync(IProducer<string, string> producer, string topic,
        int partition, string key) =>
        producer.ProduceAsync(new TopicPartition(topic, partition), new Message<string, string> { Key = key, Value = key });

    private static async Task<string[]> ReadUntilAsync(KafkaTestConsumer<string, string> consumer,
        params string[] expectedKeys)
    {
        var keys = new List<string>();

        await consumer.WaitForMessageAsync(
            result =>
            {
                keys.Add(result.Message.Key);
                return expectedKeys.All(keys.Contains);
            },
            MessageTimeout);

        return keys.Order().ToArray();
    }

    private async Task<string> RunIsolatedScenarioAsync(string value)
    {
        var topic = await fixture.Broker.CreateUniqueTopicAsync("isolated");

        using var consumer = await fixture.Broker.CreateConsumerAsync<string, string>(topic);
        using var producer = await fixture.Broker.CreateProducerAsync<string, string>(topic);

        await producer.ProduceAsync("order-1", value);

        var result = await consumer.WaitForMessageAsync(_ => true, MessageTimeout);

        Assert.Equal(topic, consumer.Topic);
        Assert.Equal("order-1", result.Message.Key);
        return result.Message.Value;
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
