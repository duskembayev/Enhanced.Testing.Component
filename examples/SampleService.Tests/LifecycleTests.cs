using Docker.DotNet;
using Docker.DotNet.Models;

namespace SampleService.Tests;

public class LifecycleTests
{
    [Fact]
    public async Task ShouldRemoveContainersOnStop()
    {
        var fixture = new SampleServiceFixture();
        IAsyncLifetime lifetime = fixture;

        await lifetime.InitializeAsync();

        string[] containerIds = [fixture.PostgreSql.Container.Id, fixture.Kafka.Container.Id, fixture.Redis.Container.Id];

        await lifetime.DisposeAsync();

        using var docker = new DockerClientBuilder().Build();

        foreach (var id in containerIds)
        {
            var containers = await docker.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["id"] = new Dictionary<string, bool> { [id] = true }
                }
            });

            Assert.Empty(containers);
        }

        Assert.Throws<InvalidOperationException>(() => fixture.PostgreSql.Container);
    }
}
