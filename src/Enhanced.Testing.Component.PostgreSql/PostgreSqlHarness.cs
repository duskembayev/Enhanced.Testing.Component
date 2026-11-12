using System.Data.Common;
using Testcontainers.PostgreSql;

namespace Enhanced.Testing.Component.PostgreSql;

/// <summary>
///     A PostgreSQL harness.
/// </summary>
public class PostgreSqlHarness : ContainerHarness<PostgreSqlContainer>
{
    /// <summary>
    ///     The default container image.
    /// </summary>
    public const string DefaultImage = "postgres:15.1";

    /// <summary>
    ///     The container image.
    /// </summary>
    public string Image { get; init; } = DefaultImage;

    /// <summary>
    ///     Customizes the container builder before the container is built.
    /// </summary>
    public Func<PostgreSqlBuilder, PostgreSqlBuilder>? ConfigureContainer { get; init; }

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
        var builder = new DbConnectionStringBuilder();

        foreach (var (key, value) in ConnectionProperties)
        {
            builder[key] = value;
        }

        builder["Host"] = Container.Hostname;
        builder["Port"] = Container.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort).ToString();
        builder["Database"] = Database;
        builder["Username"] = Username;
        builder["Password"] = Password;

        return builder.ConnectionString;
    }

    /// <inheritdoc />
    protected override PostgreSqlContainer OnCreateContainer()
    {
        var builder = new PostgreSqlBuilder(Image)
            .WithUsername(Username)
            .WithPassword(Password)
            .WithDatabase(Database);

        return (ConfigureContainer?.Invoke(builder) ?? builder).Build();
    }
}
