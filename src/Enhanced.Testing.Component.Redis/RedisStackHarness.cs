using Testcontainers.Redis;

namespace Enhanced.Testing.Component.Redis;

/// <summary>
///     A RedisStack harness.
/// </summary>
public class RedisStackHarness : ContainerHarness<RedisContainer>
{
    /// <summary>
    ///     The default container image.
    /// </summary>
    public const string DefaultImage = "redis/redis-stack-server:7.4.0-v8";

    /// <summary>
    ///     The container image.
    /// </summary>
    public string Image { get; init; } = DefaultImage;

    /// <summary>
    ///     Customizes the container builder before the container is built.
    /// </summary>
    public Func<RedisBuilder, RedisBuilder>? ConfigureContainer { get; init; }

    /// <summary>
    ///     The database index the connection string selects (<c>defaultDatabase</c>). When null, database 0 is used.
    /// </summary>
    /// <remarks>
    ///     Give each fixture its own index to isolate the application's keys while sharing one Redis server. Redis has
    ///     16 databases (0-15) unless its <c>databases</c> setting is changed; commands to an index outside that range
    ///     fail.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown if the value is negative.
    /// </exception>
    public int? DefaultDatabase
    {
        get => _defaultDatabase;
        init => _defaultDatabase = RedisConnectionString.ValidateDatabase(value, nameof(DefaultDatabase));
    }

    private readonly int? _defaultDatabase;

    /// <summary>
    ///     Keeps the image's default persistence (RDB snapshots). By default (<see langword="false" />)
    ///     persistence is disabled: no RDB snapshots and no append-only file.
    /// </summary>
    public bool Durable { get; init; }

    /// <inheritdoc />
    public override string GetConnectionString() =>
        RedisConnectionString.Build(Container.Hostname, Container.GetMappedPublicPort(RedisBuilder.RedisPort),
            DefaultDatabase);

    /// <inheritdoc />
    protected override RedisContainer OnCreateContainer()
    {
        var builder = new RedisBuilder(Image);

        if (!Durable)
        {
            builder = builder.WithEnvironment("REDIS_ARGS", "--save --appendonly no");
        }

        return (ConfigureContainer?.Invoke(builder) ?? builder).Build();
    }
}
