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
    ///     the data directory (<c>/var/lib/postgresql/data</c>) is on tmpfs and these settings are off, which is
    ///     faster but keeps the data in memory and loses it when the container stops. Set it to
    ///     <see langword="true" /> when <see cref="ConfigureContainer" /> mounts a volume at the data directory.
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
    public override string GetConnectionString() => GetConnectionString(Database);

    /// <summary>
    ///     Gets the connection string for a database in the container.
    /// </summary>
    /// <param name="database">
    ///     The database name.
    /// </param>
    /// <returns>
    ///     The connection string.
    /// </returns>
    public string GetConnectionString(string database) => BuildConnectionString(database, pooling: null);

    /// <summary>
    ///     Creates a template database: creates an empty database (dropping an existing one with the same name), runs
    ///     <paramref name="seed" /> against it, then marks it as a template and closes its connections.
    /// </summary>
    /// <param name="template">
    ///     The template database name.
    /// </param>
    /// <param name="seed">
    ///     Creates the schema and data. Receives a non-pooled connection string to the template database.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    public async Task CreateTemplateAsync(
        string template,
        Func<string, Task> seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);

        await DropDatabaseAsync(template, cancellationToken).ConfigureAwait(false);
        await ExecuteSqlAsync(cancellationToken, $"CREATE DATABASE {QuoteIdentifier(template)}").ConfigureAwait(false);

        await seed(BuildConnectionString(template, pooling: false)).ConfigureAwait(false);

        await ExecuteSqlAsync(
                cancellationToken,
                $"ALTER DATABASE {QuoteIdentifier(template)} WITH IS_TEMPLATE true ALLOW_CONNECTIONS false",
                TerminateConnectionsSql(template))
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates a database, optionally as a copy of a template database, and returns its connection string.
    /// </summary>
    /// <param name="database">
    ///     The database name.
    /// </param>
    /// <param name="template">
    ///     The template database to copy, for example one created by <see cref="CreateTemplateAsync" />.
    ///     When <see langword="null" />, the database is created empty.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    /// <returns>
    ///     The connection string for the new database.
    /// </returns>
    public async Task<string> CreateDatabaseAsync(
        string database,
        string? template = null,
        CancellationToken cancellationToken = default)
    {
        var sql = $"CREATE DATABASE {QuoteIdentifier(database)}";

        if (template is not null)
        {
            sql += $" TEMPLATE {QuoteIdentifier(template)}";
        }

        await ExecuteSqlAsync(cancellationToken, sql).ConfigureAwait(false);
        return GetConnectionString(database);
    }

    /// <summary>
    ///     Drops a database if it exists, closing its connections. Template databases are dropped too.
    /// </summary>
    /// <param name="database">
    ///     The database name.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    public Task DropDatabaseAsync(string database, CancellationToken cancellationToken = default) =>
        ExecuteSqlAsync(
            cancellationToken,
            $"UPDATE pg_database SET datistemplate = false WHERE datname = {QuoteLiteral(database)}",
            $"DROP DATABASE IF EXISTS {QuoteIdentifier(database)} WITH (FORCE)");

    private string BuildConnectionString(string database, bool? pooling)
    {
        var builder = new DbConnectionStringBuilder();

        foreach (var (key, value) in ConnectionProperties)
        {
            builder[key] = value;
        }

        builder["Host"] = Container.Hostname;
        builder["Port"] = Container.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort).ToString();
        builder["Database"] = database;
        builder["Username"] = Username;
        builder["Password"] = Password;

        if (pooling.HasValue)
        {
            builder["Pooling"] = pooling.Value ? "true" : "false";
        }

        return builder.ConnectionString;
    }

    private async Task ExecuteSqlAsync(CancellationToken cancellationToken, params string[] statements)
    {
        List<string> command = ["psql", "--username", Username, "--dbname", Database, "--set", "ON_ERROR_STOP=1"];

        foreach (var statement in statements)
        {
            command.Add("--command");
            command.Add(statement);
        }

        var result = await Container.ExecAsync(command, cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"PostgreSQL command failed: {result.Stderr.Trim()}");
        }
    }

    private static string TerminateConnectionsSql(string database) =>
        "SELECT pg_terminate_backend(pid) FROM pg_stat_activity " +
        $"WHERE datname = {QuoteLiteral(database)} AND pid <> pg_backend_pid()";

    private static string QuoteIdentifier(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static string QuoteLiteral(string literal) => "'" + literal.Replace("'", "''") + "'";

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

        return (ConfigureContainer?.Invoke(builder) ?? builder).Build();
    }
}
