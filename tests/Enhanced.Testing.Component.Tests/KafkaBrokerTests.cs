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
    public async Task ShouldReadToEndOfTopic()
    {
        var topic = await fixture.Broker.CreateUniqueTopicAsync("read-to-end");

        using var consumer = await fixture.Broker.CreateConsumerAsync<string, string>(topic);
        using var producer = await fixture.Broker.CreateProducerAsync<string, string>(topic);

        Assert.Empty(await consumer.ReadToEndAsync());

        await producer.ProduceAsync("first", "1");
        await producer.ProduceAsync("second", "2");

        Assert.Equal(["first", "second"], (await consumer.ReadToEndAsync()).Select(result => result.Message.Key));
        Assert.Empty(await consumer.ReadToEndAsync());

        await producer.ProduceAsync("third", "3");
        consumer.SeekToBeginning();

        Assert.Equal(["first", "second", "third"],
            (await consumer.ReadToEndAsync()).Select(result => result.Message.Key));
    }

    [Fact]
    public async Task ShouldReadToEndAfterWaitingAndConsuming()
    {
        var topic = await fixture.Broker.CreateUniqueTopicAsync("read-to-end");

        using var consumer = await fixture.Broker.CreateConsumerAsync<string, string>(topic);
        using var producer = await fixture.Broker.CreateProducerAsync<string, string>(topic);

        await producer.ProduceAsync("first", "1");
        await consumer.WaitForKeyAsync("first", MessageTimeout);

        Assert.Null(consumer.Consume(TimeSpan.FromMilliseconds(300)));
        Assert.Empty(await consumer.ReadToEndAsync());

        await producer.ProduceAsync("second", "2");

        Assert.Equal(["second"], (await consumer.ReadToEndAsync()).Select(result => result.Message.Key));
    }

    [Fact]
    public async Task ShouldReadToEndOfTopicWithTransactions()
    {
        var topic = await fixture.Broker.CreateUniqueTopicAsync("read-to-end");
        var transactionTimeout = TimeSpan.FromSeconds(30);

        using var consumer = await fixture.Broker.CreateConsumerAsync<string, string>(topic);
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = fixture.Broker.GetConnectionString(),
            TransactionalId = Guid.NewGuid().ToString("N")
        }).Build();

        producer.InitTransactions(transactionTimeout);

        producer.BeginTransaction();
        await producer.ProduceAsync(topic, new Message<string, string> { Key = "committed", Value = "1" });
        producer.CommitTransaction(transactionTimeout);

        producer.BeginTransaction();
        await producer.ProduceAsync(topic, new Message<string, string> { Key = "aborted", Value = "2" });
        producer.AbortTransaction(transactionTimeout);

        Assert.Equal(["committed"], (await consumer.ReadToEndAsync()).Select(result => result.Message.Key));
        Assert.Empty(await consumer.ReadToEndAsync());
    }

    [Fact]
    public async Task ShouldReadToEndWithHarnessConsumer()
    {
        var key = Guid.NewGuid().ToString("N");

        await fixture.Producer.ProduceAsync(key, "value");

        Assert.Contains(await fixture.Consumer.ReadToEndAsync(), result => result.Message.Key == key);
        Assert.Empty(await fixture.Consumer.ReadToEndAsync());
        Assert.Null(fixture.Consumer.Consume(TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task ShouldWaitUntilConsumerGroupIsAssigned()
    {
        var topic = await fixture.Broker.CreateUniqueTopicAsync("assignment");
        var groupId = "group-" + Guid.NewGuid().ToString("N");
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var stop = new CancellationTokenSource();

        var application = Task.Run(() =>
        {
            using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
            {
                BootstrapServers = fixture.Broker.GetConnectionString(),
                GroupId = groupId,
                AutoOffsetReset = AutoOffsetReset.Latest
            }).Build();

            consumer.Subscribe(topic);

            try
            {
                while (true)
                {
                    received.TrySetResult(consumer.Consume(stop.Token).Message.Key);
                }
            }
            catch (OperationCanceledException)
            {
                consumer.Close();
            }
        });

        try
        {
            await fixture.Broker.WaitForAssignmentAsync(groupId, topic, TimeSpan.FromSeconds(30));

            using var producer = await fixture.Broker.CreateProducerAsync<string, string>(topic);
            await producer.ProduceAsync("after-assignment", "value");

            Assert.Equal("after-assignment", await received.Task.WaitAsync(MessageTimeout));
        }
        finally
        {
            await stop.CancelAsync();
            await application;
        }
    }

    [Fact]
    public async Task ShouldFailWhenConsumerGroupIsNotAssigned()
    {
        var topic = await fixture.Broker.CreateUniqueTopicAsync("assignment");
        var groupId = "missing-" + Guid.NewGuid().ToString("N");

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => fixture.Broker.WaitForAssignmentAsync(groupId, topic, TimeSpan.FromSeconds(1)));

        Assert.Contains(groupId, exception.Message);
        Assert.Contains(topic, exception.Message);
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
