using Enhanced.Testing.Component;
using Enhanced.Testing.Component.Redis;

namespace SampleService.Tests;

public class ContainerConfigurationTests
{
    [Fact]
    public async Task ShouldUseConfiguredImageAndBuilder()
    {
        var redis = new RedisHarness
        {
            Image = "redis:7.4",
            ConfigureContainer = builder => builder.WithEnvironment("SAMPLE_VARIABLE", "configured")
        };

        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(redis)
                                        .Build();

        await component.StartAsync(default);

        try
        {
            Assert.Equal("redis:7.4", redis.Container.Image.FullName);

            var result = await redis.Container.ExecAsync(["printenv", "SAMPLE_VARIABLE"]);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("configured", result.Stdout.Trim());
        }
        finally
        {
            await component.StopAsync(default);
        }
    }
}
