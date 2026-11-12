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

    private const string DataDirectory = "/var/lib/postgresql/data";

    /// <summary>
    ///     The container image.
    /// </summary>
    public string Image { get; init; } = DefaultImage;

    /// <summary>
    ///     Customizes the container builder before the container is built.
    /// </summary>
    public Func<PostgreSqlBuilder, PostgreSqlBuilder>? ConfigureContainer { get; init; }

    /// <summary>
    ///     Keeps the server durable: the data directory stays on the container filesystem and <c>fsync</c>,
    ///     <c>full_page_writes</c> and <c>synchronous_commit</c> are on. By default (<see langword="false" />)
    ///     the data directory is on tmpfs and these settings are off, which is faster but loses data on a crash.
    /// </summary>
    public bool Durable { get; init; }

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
    protected override bool SupportsReuse => true;

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

        builder = Durable
            ? builder.WithCommand("-c", "fsync=on", "-c", "full_page_writes=on", "-c", "synchronous_commit=on")
            : builder.WithEnvironment("PGDATA", DataDirectory).WithTmpfsMount(DataDirectory);

        if (Reuse)
        {
            builder = builder.WithReuse(true);
        }

        return (ConfigureContainer?.Invoke(builder) ?? builder).Build();
    }
}
