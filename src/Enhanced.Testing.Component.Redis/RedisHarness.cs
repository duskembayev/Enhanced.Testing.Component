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
    ///     Keeps the image's default persistence (RDB snapshots). By default (<see langword="false" />)
    ///     persistence is disabled: no RDB snapshots and no append-only file.
    /// </summary>
    public bool Durable { get; init; }

    /// <inheritdoc />
    public override string GetConnectionString() =>
        $"{Container.Hostname}:{Container.GetMappedPublicPort(RedisBuilder.RedisPort)}";

    /// <inheritdoc />
    protected override RedisContainer OnCreateContainer()
    {
        var builder = new RedisBuilder(Image);

        if (!Durable)
        {
            builder = builder.WithCommand("--save", "", "--appendonly", "no");
        }

        return (ConfigureContainer?.Invoke(builder) ?? builder).Build();
    }
}
