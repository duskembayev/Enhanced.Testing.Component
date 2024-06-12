namespace SampleService.Tests;

public class SampleTests(SampleServiceFixture fixture) : IClassFixture<SampleServiceFixture>
{
    [Fact]
    public async Task ShouldBeHealthy()
    {
        var httpClient = fixture.HttpClient.CreateClient();

        var response = await httpClient.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadAsStringAsync();

        Assert.Equal("Healthy", status);
    }

    [Fact]
    public async Task ShouldSayHello()
    {
        var client = fixture.GrpcClient.CreateClient<Greeter.GreeterClient>();

        var reply = await client.SayHelloAsync(new HelloRequest { Name = "World" });

        Assert.Equal("Hello World", reply.Message);
    }
}
