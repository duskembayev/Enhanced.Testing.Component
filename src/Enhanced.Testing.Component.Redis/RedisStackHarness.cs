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
    public const string DefaultImage = "redis/redis-stack-server:latest";

    /// <summary>
    ///     The container image.
    /// </summary>
    public string Image { get; init; } = DefaultImage;

    /// <summary>
    ///     Customizes the container builder before the container is built.
    /// </summary>
    public Func<RedisBuilder, RedisBuilder>? ConfigureContainer { get; init; }

    /// <inheritdoc />
    public override string GetConnectionString() =>
        $"{Container.Hostname}:{Container.GetMappedPublicPort(RedisBuilder.RedisPort)}";

    /// <inheritdoc />
    protected override RedisContainer OnCreateContainer()
    {
        var builder = new RedisBuilder(Image);
        return (ConfigureContainer?.Invoke(builder) ?? builder).Build();
    }
}
