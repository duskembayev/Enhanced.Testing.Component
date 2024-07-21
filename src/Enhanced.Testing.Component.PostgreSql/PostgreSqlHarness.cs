using Testcontainers.PostgreSql;

namespace Enhanced.Testing.Component.PostgreSql;

/// <summary>
///     A PostgreSQL harness.
/// </summary>
public class PostgreSqlHarness : ContainerHarness<PostgreSqlContainer>
{
    /// <summary>
    ///     The username to use when connecting to the PostgreSQL database.
    /// </summary>
    public string Username { get; init; } = PostgreSqlBuilder.DefaultUsername;

    /// <summary>
    ///     The password to use when connecting to the PostgreSQL database.
    /// </summary>
    public string Password { get; init; } = PostgreSqlBuilder.DefaultPassword;

    /// <summary>
    ///     The database to use when connecting to the PostgreSQL database.
    /// </summary>
    public string Database { get; init; } = PostgreSqlBuilder.DefaultDatabase;

    /// <summary>
    ///     The connection string properties.
    /// </summary>
    public IDictionary<string, string> ConnectionProperties { get; init; } = new Dictionary<string, string>();

    /// <inheritdoc />
    public override string GetConnectionString()
    {
        var properties = new Dictionary<string, string>(ConnectionProperties)
        {
            ["Host"] = Container.Hostname,
            ["Port"] = Container.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort).ToString(),
            ["Database"] = Database,
            ["Username"] = Username,
            ["Password"] = Password
        };
        return string.Join(";", properties.Select(property => string.Join("=", property.Key, property.Value)));
    }

    /// <inheritdoc />
    protected override PostgreSqlContainer OnCreateContainer() =>
        new PostgreSqlBuilder()
            .WithUsername(Username)
            .WithPassword(Password)
            .WithDatabase(Database)
            .Build();
}
