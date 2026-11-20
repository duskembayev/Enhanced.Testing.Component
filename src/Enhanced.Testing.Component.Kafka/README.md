# Enhanced.Testing.Component.Kafka

[![NuGet](https://img.shields.io/nuget/v/Enhanced.Testing.Component.Kafka)](https://www.nuget.org/packages/Enhanced.Testing.Component.Kafka)

Kafka harnesses for [Enhanced.Testing.Component](https://github.com/duskembayev/Enhanced.Testing.Component): an Apache
Kafka, Confluent or Redpanda broker in a container, producer and consumer harnesses, waiting for a specific message,
and a topic per test.

```shell
dotnet add package Enhanced.Testing.Component.Kafka
```

## Usage

```csharp
var kafka = new KafkaHarness { Topics = ["orders"], ConnectionName = "Kafka" };
var consumer = new KafkaConsumerHarness<string, string>(kafka) { Topic = "orders" };
var producer = new KafkaProducerHarness<string, string>(kafka) { Topic = "orders" };

var component = ComponentBuilder.Create<Program>()
                                .AddHarness(kafka)
                                .AddHarness(consumer)
                                .AddHarness(producer)
                                .AddHarness(http)
                                .Build();
```

The application reads its bootstrap servers from `ConnectionStrings:Kafka`. Topics in `Topics` are created on start
(existing topics are kept), and the harness waits until they have a partition leader. They get the broker's default
number of partitions, which is one; set `TopicPartitions` on the broker harness to create them with more.

In a test, check what the application produced, or send it a message:

```csharp
await fixture.Http.CreateClient().PostAsJsonAsync("/orders", new { Id = 42 });

var message = await fixture.Consumer.WaitForKeyAsync("order-42", TimeSpan.FromSeconds(10));

await fixture.Producer.ProduceAsync("order-43", "created");
```

## Waiting for your message

The consumer is positioned at the end of the topic on start. Prefer waiting for *your* message over reading the next
one, so messages left by other tests or a slow producer don't make the test flaky:

```csharp
var message = await consumer.WaitForKeyAsync("order-42", TimeSpan.FromSeconds(10));
var result = await consumer.WaitForMessageAsync(r => r.Message.Value.Contains("42"), TimeSpan.FromSeconds(10));
```

Non-matching messages are skipped; a `TimeoutException` reports how many.

`SeekToBeginning` and `SeekToEnd` move the position the wait starts from. To return to a message you already know,
seek to its partition and offset:

```csharp
consumer.Seek(result.TopicPartitionOffset);
```

`Seek(Offset, ...)` was removed in 1.5; use the methods above instead.

## A topic per test

To let tests share one broker and still run in parallel, give each test its own topic. `CreateUniqueTopicAsync`
creates a topic with a unique name and waits for its leader; `CreateConsumerAsync` and `CreateProducerAsync` return
disposable clients bound to it, with the same `WaitFor*` API:

```csharp
var topic = await kafka.CreateUniqueTopicAsync("orders");
using var consumer = await kafka.CreateConsumerAsync<string, string>(topic);
using var producer = await kafka.CreateProducerAsync<string, string>(topic);

await producer.ProduceAsync("order-42", "created");
var message = await consumer.WaitForKeyAsync("order-42", TimeSpan.FromSeconds(10));
```

## Topics with several partitions

Consumers, `KafkaConsumerHarness` included, read every partition the topic has when they start (before 1.5, only
partition 0). Messages from different partitions arrive in no particular order relative to each other, so wait for a
key or a predicate. Partitions added later are not read.

```csharp
var kafka = new KafkaHarness { Topics = ["orders"], TopicPartitions = 3 };

await kafka.CreateTopicAsync("payments", 3);
var topic = await kafka.CreateUniqueTopicAsync("orders", 3);
```

`SeekToBeginning(partition)`, `SeekToEnd(partition)` and `Seek(TopicPartitionOffset)` move one partition and leave
the positions of the others as they are.

## Choosing a broker

Any Kafka-compatible broker works with the producer and consumer harnesses (they accept `IKafkaHarness`).
`KafkaHarness` uses `apache/kafka-native` (a GraalVM build of Apache Kafka) by default: it starts fastest and has the
smallest image. Use `KafkaHarness.ConfluentImage` for the Confluent Platform image, or `RedpandaHarness` for
applications that run Redpanda in production:

```csharp
var kafka = new KafkaHarness { Topics = ["orders"] };
var confluent = new KafkaHarness { Image = KafkaHarness.ConfluentImage, Topics = ["orders"] };
var redpanda = new RedpandaHarness { Topics = ["orders"], ConnectionName = "Kafka" };
var consumer = new KafkaConsumerHarness<string, string>(redpanda) { Topic = "orders" };
```

The native image has no `kafka-*` command-line tools; the harnesses create topics through the Kafka admin API.
`ConnectionName`, `Image`, `ConfigureContainer` and `StartConcurrently` are shared by all container harnesses; see
[Enhanced.Testing.Component](https://github.com/duskembayev/Enhanced.Testing.Component/blob/main/src/Enhanced.Testing.Component/README.md#container-harnesses).
