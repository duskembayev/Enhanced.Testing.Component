using Testcontainers.Redis;

namespace Enhanced.Testing.Component.Redis;

/// <summary>
///     A RedisStack harness.
/// </summary>
public class RedisStackHarness : ContainerHarness<RedisContainer>
{
    /// <inheritdoc />
    public override string GetConnectionString() =>
        $"{Container.Hostname}:{Container.GetMappedPublicPort(RedisBuilder.RedisPort)}";

    /// <inheritdoc />
    protected override RedisContainer OnCreateContainer() =>
        new RedisBuilder()
            .WithImage("redis/redis-stack-server:latest")
            .Build();
}
