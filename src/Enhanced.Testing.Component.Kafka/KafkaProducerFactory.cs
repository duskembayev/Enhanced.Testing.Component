using Confluent.Kafka;

namespace Enhanced.Testing.Component.Kafka;

internal static class KafkaProducerFactory
{
    public static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WarmUpTimeout = TimeSpan.FromSeconds(5);

    public static IProducer<TKey, TValue> Create<TKey, TValue>(string bootstrapServers, string topic,
        ISerializer<TKey>? keySerializer, ISerializer<TValue>? valueSerializer)
    {
        var producer = Build(bootstrapServers, keySerializer, valueSerializer);
        WarmUp(producer, topic);
        return producer;
    }

    public static async Task<IProducer<TKey, TValue>> CreateAsync<TKey, TValue>(string bootstrapServers,
        string topic, ISerializer<TKey>? keySerializer, ISerializer<TValue>? valueSerializer,
        CancellationToken cancellationToken)
    {
        var producer = Build(bootstrapServers, keySerializer, valueSerializer);

        try
        {
            await Task.Run(() => WarmUp(producer, topic), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            producer.Dispose();
            throw;
        }

        return producer;
    }

    private static IProducer<TKey, TValue> Build<TKey, TValue>(string bootstrapServers,
        ISerializer<TKey>? keySerializer, ISerializer<TValue>? valueSerializer)
    {
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            AllowAutoCreateTopics = false,
            Acks = Acks.All,
            LingerMs = 0,
            SocketNagleDisable = true
        };

        var producer = new ProducerBuilder<TKey, TValue>(producerConfig)
                       .SetKeySerializer(keySerializer)
                       .SetValueSerializer(valueSerializer)
                       .Build();

        return producer;
    }

    private static void WarmUp<TKey, TValue>(IProducer<TKey, TValue> producer, string topic)
    {
        using var adminClient = new DependentAdminClientBuilder(producer.Handle).Build();

        try
        {
            adminClient.GetMetadata(topic, WarmUpTimeout);
        }
        catch (KafkaException)
        {
        }
    }
}
