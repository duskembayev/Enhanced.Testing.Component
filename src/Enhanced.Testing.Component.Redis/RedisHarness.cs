using Testcontainers.Redis;

namespace Enhanced.Testing.Component.Redis;

/// <summary>
///     A Redis harness.
/// </summary>
public class RedisHarness : ContainerHarness<RedisContainer>
{
    /// <summary>
    ///     The default container image.
    /// </summary>
    public const string DefaultImage = "redis:7.0";

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
    ///     Keeps the persistence settings of the image or of the command set in <see cref="ConfigureContainer" />
    ///     (RDB snapshots by default). By default (<see langword="false" />) persistence is disabled: the harness
    ///     appends <c>--save "" --appendonly no</c> to the container command, after <see cref="ConfigureContainer" />
    ///     has run, so these two settings win over the ones in a configuration file or earlier arguments.
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
        builder = ConfigureContainer?.Invoke(builder) ?? builder;

        if (!Durable)
        {
            builder = builder.WithCommand("--save", "", "--appendonly", "no");
        }

        return builder.Build();
    }
}
