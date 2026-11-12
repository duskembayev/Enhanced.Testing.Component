using Testcontainers.Redis;

namespace Enhanced.Testing.Component.Redis;

/// <summary>
///     A Redis harness.
/// </summary>
public class RedisHarness : ContainerHarness<RedisContainer>
{
    /// <inheritdoc />
    public override string GetConnectionString() =>
        $"{Container.Hostname}:{Container.GetMappedPublicPort(RedisBuilder.RedisPort)}";

    /// <inheritdoc />
    protected override RedisContainer OnCreateContainer() => new RedisBuilder("redis:7.0").Build();
}
