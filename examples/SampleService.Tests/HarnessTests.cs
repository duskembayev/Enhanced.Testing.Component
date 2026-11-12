using Enhanced.Testing.Component;
using Enhanced.Testing.Component.Kafka;
using Enhanced.Testing.Component.PostgreSql;
using Npgsql;

namespace SampleService.Tests;

public class HarnessTests
{
    [Fact]
    public async Task ShouldConnectWithSpecialCharactersInCredentials()
    {
        var postgreSql = new PostgreSqlHarness
        {
            Password = "p;ss=w'rd\"",
            ConnectionProperties = new Dictionary<string, string> { ["Application Name"] = "tests;special=chars" }
        };

        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(postgreSql)
                                        .Build();

        try
        {
            await component.StartAsync(default);

            await using var connection = new NpgsqlConnection(postgreSql.GetConnectionString());
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand("SELECT current_setting('application_name')", connection);
            var applicationName = await command.ExecuteScalarAsync();

            Assert.Equal("tests;special=chars", applicationName);
        }
        finally
        {
            await component.StopAsync(default);
        }
    }

    [Fact]
    public async Task ShouldFailToStartWhenTopicCannotBeCreated()
    {
        var kafka = new KafkaHarness { Topics = ["invalid topic name"] };

        var component = ComponentBuilder.Create<Program>()
                                        .AddHarness(kafka)
                                        .Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => component.StartAsync(default));

        Assert.Contains("invalid topic name", exception.Message);
    }
}
