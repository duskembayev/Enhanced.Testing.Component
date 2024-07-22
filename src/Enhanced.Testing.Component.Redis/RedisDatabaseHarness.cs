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
    private IDatabase? _database;
    private ConnectionMultiplexer? _multiplexer;

    /// <summary>
    ///     The key prefix.
    /// </summary>
    public string? KeyPrefix { get; set; }

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

    /// <inheritdoc />
    protected override async Task OnStart(CancellationToken cancellationToken)
    {
        var connectionString = redisHarness.GetConnectionString();

        _multiplexer = await ConnectionMultiplexer
                             .ConnectAsync(ConfigurationOptions.Parse(connectionString!))
                             .ConfigureAwait(false);

        _database = _multiplexer.GetDatabase();

        if (KeyPrefix != null)
        {
            _database = _database.WithKeyPrefix(KeyPrefix);
        }
    }

    /// <inheritdoc />
    protected override async Task OnStop(CancellationToken cancellationToken)
    {
        if (_multiplexer != null)
        {
            await _multiplexer.CloseAsync(false).ConfigureAwait(false);
            await _multiplexer.DisposeAsync().ConfigureAwait(false);
        }
    }
}
