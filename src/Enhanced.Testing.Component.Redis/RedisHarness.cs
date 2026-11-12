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
    ///     Give each fixture its own index to isolate the application's keys while sharing one Redis server.
    /// </remarks>
    public int? DefaultDatabase { get; init; }

    /// <inheritdoc />
    public override string GetConnectionString() =>
        RedisConnectionString.Build(Container.Hostname, Container.GetMappedPublicPort(RedisBuilder.RedisPort),
            DefaultDatabase);

    /// <inheritdoc />
    protected override RedisContainer OnCreateContainer()
    {
        var builder = new RedisBuilder(Image);
        return (ConfigureContainer?.Invoke(builder) ?? builder).Build();
    }
}
