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
    private static readonly TimeSpan AssignmentPollInterval = TimeSpan.FromMilliseconds(100);

    public static async Task<bool> TryCreateTopicAsync(string bootstrapServers, string topicName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CreateTopicAsync(bootstrapServers, topicName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is KafkaException or TimeoutException)
        {
            return false;
        }
    }

    public static async Task EnsureTopicAsync(string bootstrapServers, string topicName,
        CancellationToken cancellationToken)
    {
        try
        {
            await CreateTopicAsync(bootstrapServers, topicName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is KafkaException or TimeoutException)
        {
            throw new InvalidOperationException(
                $"Failed to create Kafka topic '{topicName}': {exception.Message}", exception);
        }
    }

    public static async Task WaitForAssignmentAsync(string bootstrapServers, string groupId, string topicName,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var adminClient = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers })
            .Build();

        var stopwatch = Stopwatch.StartNew();
        var lastState = "the group was not found";
        var assigned = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? state;

            try
            {
                state = await GetPendingAssignmentAsync(adminClient, groupId, topicName).ConfigureAwait(false);
            }
            catch (KafkaException exception)
            {
                state = exception.Error.Reason;
            }

            if (state is null && assigned)
            {
                return;
            }

            assigned = state is null;
            lastState = state ?? lastState;

            if (stopwatch.Elapsed >= timeout)
            {
                throw new TimeoutException(
                    $"Consumer group '{groupId}' did not get the partitions of Kafka topic '{topicName}' within " +
                    $"{timeout.TotalMilliseconds:0} ms: {lastState}.");
            }

            await Task.Delay(AssignmentPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string?> GetPendingAssignmentAsync(IAdminClient adminClient, string groupId,
        string topicName)
    {
        var topic = adminClient.GetMetadata(topicName, MetadataTimeout).Topics.FirstOrDefault(t => t.Topic == topicName);

        if (topic is null || topic.Error.IsError || topic.Partitions.Count == 0)
        {
            return "the topic does not exist";
        }

        var options = new DescribeConsumerGroupsOptions { RequestTimeout = AttemptTimeout };
        var result = await adminClient.DescribeConsumerGroupsAsync([groupId], options).ConfigureAwait(false);
        var group = result.ConsumerGroupDescriptions.FirstOrDefault();

        if (group is null)
        {
            return "the group was not found";
        }

        if (group.State != ConsumerGroupState.Stable)
        {
            return $"the group is in the {group.State} state";
        }

        var assigned = group.Members
                            .SelectMany(member => member.Assignment?.TopicPartitions ?? [])
                            .Where(partition => partition.Topic == topicName)
                            .Select(partition => partition.Partition.Value)
                            .Distinct()
                            .Count();

        return assigned == topic.Partitions.Count
            ? null
            : $"{assigned} of {topic.Partitions.Count} partition(s) are assigned";
    }

    private static async Task<bool> CreateTopicAsync(string bootstrapServers, string topicName,
        CancellationToken cancellationToken)
    {
        var config = new AdminClientConfig { BootstrapServers = bootstrapServers };
        var stopwatch = Stopwatch.StartNew();
        var retried = false;
        bool created;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var error = await TryCreateOnceAsync(config, topicName, cancellationToken).ConfigureAwait(false);

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
        await WaitForLeadersAsync(adminClient, topicName, cancellationToken).ConfigureAwait(false);
        return created;
    }

    private static async Task<Error> TryCreateOnceAsync(AdminClientConfig config, string topicName,
        CancellationToken cancellationToken)
    {
        using var adminClient = new AdminClientBuilder(config).Build();
        var options = new CreateTopicsOptions { RequestTimeout = AttemptTimeout, OperationTimeout = AttemptTimeout };

        try
        {
            await adminClient.CreateTopicsAsync([new TopicSpecification { Name = topicName }], options)
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

    private static async Task WaitForLeadersAsync(IAdminClient adminClient, string topicName,
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
                else if (topic.Partitions.Count == 0)
                {
                    lastError = "topic has no partitions";
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
