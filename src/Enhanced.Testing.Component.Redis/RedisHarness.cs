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
    ///     Keeps the persistence settings of the image or of the command set in <see cref="ConfigureContainer" />
    ///     (RDB snapshots by default). By default (<see langword="false" />) persistence is disabled: the harness
    ///     appends <c>--save "" --appendonly no</c> to the container command, after <see cref="ConfigureContainer" />
    ///     has run, so these two settings win over the ones in a configuration file or earlier arguments.
    /// </summary>
    public bool Durable { get; init; }

    /// <inheritdoc />
    protected override bool SupportsReuse => true;

    /// <inheritdoc />
    public override string GetConnectionString() =>
        $"{Container.Hostname}:{Container.GetMappedPublicPort(RedisBuilder.RedisPort)}";

    /// <inheritdoc />
    protected override RedisContainer OnCreateContainer()
    {
        var builder = new RedisBuilder(Image);

        if (Reuse)
        {
            builder = builder.WithReuse(true);
        }

        builder = ConfigureContainer?.Invoke(builder) ?? builder;

        if (!Durable)
        {
            builder = builder.WithCommand("--save", "", "--appendonly", "no");
        }

        return builder.Build();
    }
}
