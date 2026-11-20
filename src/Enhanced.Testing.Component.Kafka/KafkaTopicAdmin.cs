using System.Diagnostics;
using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace Enhanced.Testing.Component.Kafka;

internal static class KafkaTopicAdmin
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    public static async Task<bool> TryCreateTopicAsync(string bootstrapServers, string topicName, int? partitions,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CreateTopicAsync(bootstrapServers, topicName, partitions, cancellationToken)
                       .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is KafkaException or TimeoutException)
        {
            return false;
        }
    }

    public static async Task EnsureTopicAsync(string bootstrapServers, string topicName, int? partitions,
        CancellationToken cancellationToken)
    {
        try
        {
            await CreateTopicAsync(bootstrapServers, topicName, partitions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is KafkaException or TimeoutException)
        {
            throw new InvalidOperationException(
                $"Failed to create Kafka topic '{topicName}': {exception.Message}", exception);
        }
    }

    public static int? ValidatePartitions(int? partitions, string parameterName)
    {
        if (partitions < 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, partitions, "A topic has at least one partition.");
        }

        return partitions;
    }

    private static async Task<bool> CreateTopicAsync(string bootstrapServers, string topicName, int? partitions,
        CancellationToken cancellationToken)
    {
        var config = new AdminClientConfig { BootstrapServers = bootstrapServers };
        var stopwatch = Stopwatch.StartNew();
        var retried = false;
        bool created;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var error = await TryCreateOnceAsync(config, topicName, partitions, cancellationToken)
                            .ConfigureAwait(false);

            if (!error.IsError)
            {
                created = true;
                break;
            }

            if (error.Code == ErrorCode.TopicAlreadyExists)
            {
                created = retried;
                break;
            }

            if (!IsTransient(error) || stopwatch.Elapsed >= OperationTimeout)
            {
                throw new KafkaException(error);
            }

            retried = true;
            await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
        }

        using var adminClient = new AdminClientBuilder(config).Build();
        await WaitForLeadersAsync(adminClient, topicName, created ? partitions : null, cancellationToken)
            .ConfigureAwait(false);
        return created;
    }

    private static async Task<Error> TryCreateOnceAsync(AdminClientConfig config, string topicName, int? partitions,
        CancellationToken cancellationToken)
    {
        using var adminClient = new AdminClientBuilder(config).Build();
        var options = new CreateTopicsOptions { RequestTimeout = AttemptTimeout, OperationTimeout = AttemptTimeout };

        try
        {
            var topic = new TopicSpecification { Name = topicName, NumPartitions = partitions ?? -1 };

            await adminClient.CreateTopicsAsync([topic], options)
                             .WaitAsync(cancellationToken)
                             .ConfigureAwait(false);

            return new Error(ErrorCode.NoError);
        }
        catch (CreateTopicsException exception)
        {
            return exception.Results.Select(r => r.Error).FirstOrDefault(e => e.IsError) ?? new Error(ErrorCode.NoError);
        }
        catch (KafkaException exception)
        {
            return exception.Error;
        }
    }

    private static bool IsTransient(Error error)
    {
        return error.Code is ErrorCode.Local_TimedOut or ErrorCode.Local_Transport
            or ErrorCode.Local_AllBrokersDown or ErrorCode.RequestTimedOut or ErrorCode.NotController
            or ErrorCode.LeaderNotAvailable or ErrorCode.BrokerNotAvailable;
    }

    private static async Task WaitForLeadersAsync(IAdminClient adminClient, string topicName, int? partitions,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var lastError = "no metadata received";

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var metadata = adminClient.GetMetadata(topicName, MetadataTimeout);
                var topic = metadata.Topics.FirstOrDefault(t => t.Topic == topicName);

                if (topic is null)
                {
                    lastError = "topic is missing from metadata";
                }
                else if (topic.Error.IsError)
                {
                    lastError = topic.Error.Reason;
                }
                else if (topic.Partitions.Count < (partitions ?? 1))
                {
                    lastError = "topic does not have all its partitions yet";
                }
                else if (topic.Partitions.Any(p => p.Leader < 0 || p.Error.IsError))
                {
                    lastError = "partition leader is not available";
                }
                else
                {
                    return;
                }
            }
            catch (KafkaException exception)
            {
                lastError = exception.Error.Reason;
            }

            if (stopwatch.Elapsed >= OperationTimeout)
            {
                throw new TimeoutException(
                    $"Kafka topic '{topicName}' is not ready after {OperationTimeout.TotalSeconds:0} s: {lastError}.");
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
