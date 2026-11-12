using System.Text;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;
using Testcontainers.Redis;

namespace Enhanced.Testing.Component.Redis;

/// <summary>
///     A Redis database harness.
/// </summary>
/// <param name="redisHarness">
///     The Redis harness.
/// </param>
public class RedisDatabaseHarness(ContainerHarness<RedisContainer> redisHarness) : Harness
{
    private const int DeleteBatchSize = 1000;

    private IDatabase? _database;
    private IDatabase? _unprefixedDatabase;
    private string? _activeKeyPrefix;
    private ConnectionMultiplexer? _multiplexer;

    /// <summary>
    ///     The key prefix.
    /// </summary>
    public string? KeyPrefix { get; set; }

    /// <summary>
    ///     The database index to use. When null, the default database of the connection string is used.
    /// </summary>
    public int? DatabaseNumber { get; set; }

    /// <summary>
    ///     The Redis connection multiplexer.
    /// </summary>
    public ConnectionMultiplexer Multiplexer
    {
        get
        {
            ThrowIfComponentNotStarted();
            return _multiplexer!;
        }
    }

    /// <summary>
    ///     The Redis database.
    /// </summary>
    public IDatabase Database
    {
        get
        {
            ThrowIfComponentNotStarted();
            return _database!;
        }
    }

    /// <summary>
    ///     Deletes the data of this harness: the keys that start with <see cref="KeyPrefix" /> when it is set; otherwise,
    ///     every key in the database.
    /// </summary>
    /// <returns>
    ///     A task that completes when the keys are deleted.
    /// </returns>
    public async Task FlushAsync()
    {
        ThrowIfComponentNotStarted();

        var multiplexer = _multiplexer!;
        var database = _unprefixedDatabase!;
        var keyPrefix = _activeKeyPrefix;

        foreach (var endPoint in multiplexer.GetEndPoints())
        {
            var server = multiplexer.GetServer(endPoint);

            if (server.IsReplica || !server.IsConnected)
            {
                continue;
            }

            if (keyPrefix is null)
            {
                await server.FlushDatabaseAsync(database.Database).ConfigureAwait(false);
                continue;
            }

            var batch = new List<RedisKey>(DeleteBatchSize);

            await foreach (var key in server.KeysAsync(database.Database, EscapePattern(keyPrefix) + "*",
                               DeleteBatchSize).ConfigureAwait(false))
            {
                batch.Add(key);

                if (batch.Count == DeleteBatchSize)
                {
                    await database.KeyDeleteAsync(batch.ToArray()).ConfigureAwait(false);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await database.KeyDeleteAsync(batch.ToArray()).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    protected override async Task OnStart(CancellationToken cancellationToken)
    {
        var connectionString = redisHarness.GetConnectionString();
        var options = ConfigurationOptions.Parse(connectionString!);
        options.AllowAdmin = true;

        _multiplexer = await ConnectionMultiplexer
                             .ConnectAsync(options)
                             .ConfigureAwait(false);

        _unprefixedDatabase = _multiplexer.GetDatabase(DatabaseNumber ?? -1);
        _database = _unprefixedDatabase;
        _activeKeyPrefix = KeyPrefix;

        if (_activeKeyPrefix != null)
        {
            _database = _database.WithKeyPrefix(_activeKeyPrefix);
        }
    }

    /// <inheritdoc />
    protected override async Task OnStop(CancellationToken cancellationToken)
    {
        var multiplexer = _multiplexer;

        if (multiplexer is null)
        {
            return;
        }

        _multiplexer = null;
        _database = null;
        _unprefixedDatabase = null;
        _activeKeyPrefix = null;

        try
        {
            await multiplexer.CloseAsync(false).ConfigureAwait(false);
        }
        finally
        {
            await multiplexer.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string EscapePattern(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var c in value)
        {
            if (c is '\\' or '*' or '?' or '[' or ']')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
