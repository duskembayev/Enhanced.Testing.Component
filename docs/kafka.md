# Kafka

[← Back to README](../README.md)

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

## Producing and consuming

`KafkaHarness` and `RedpandaHarness` create topics on start (`Topics`; existing topics are kept) and wait until they
have a partition leader. `KafkaProducerHarness` sends messages to the application, and `KafkaConsumerHarness` reads
what it produced. The consumer is positioned at the end of the topic on start; prefer waiting for *your* message
over reading the next one, so messages left by other tests or a slow producer don't make the test flaky:

```csharp
var message = await consumer.WaitForKeyAsync("order-42", TimeSpan.FromSeconds(10));
var result = await consumer.WaitForMessageAsync(r => r.Message.Value.Contains("42"), TimeSpan.FromSeconds(10));
```

Non-matching messages are skipped; a `TimeoutException` reports how many. `SeekToBeginning`/`SeekToEnd` move the
position the wait starts from.

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
