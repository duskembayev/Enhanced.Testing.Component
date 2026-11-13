using Microsoft.AspNetCore.Hosting;

namespace Enhanced.Testing.Component.PostgreSql;

/// <summary>
///     A database of its own for a component, created in a shared PostgreSQL container when the component starts and
///     dropped when it stops. Use it with a <see cref="PostgreSqlHarness" /> shared by a
///     <see cref="IComponentEnvironment" /> or by a test fixture, so every component gets clean data without starting
///     a container.
/// </summary>
/// <remarks>
///     The database is dropped when the component stops, also when its creation was interrupted, so a cancelled start
///     does not leave it behind.
/// </remarks>
/// <param name="postgreSql">
///     The PostgreSQL harness that runs the container. It must be started before this harness: share it through an
///     environment, or add it to the same component before this harness.
/// </param>
public class PostgreSqlDatabaseHarness(PostgreSqlHarness postgreSql) : Harness
{
    private string? _database;
    private bool _created;

    /// <summary>
    ///     The name of the connection string to add to the configuration of the component. It overrides a connection
    ///     string with the same name set by the <see cref="PostgreSqlHarness" />.
    /// </summary>
    public string? ConnectionName { get; init; }

    /// <summary>
    ///     The database name. By default a unique name is generated every time the harness starts, so a harness reused
    ///     by several components never hands out the name of a dropped database.
    /// </summary>
    /// <remarks>
    ///     With a fixed name, connections pooled by the previous application instance are closed when the database is
    ///     dropped, and the next instance can get such a closed connection from the pool. Prefer the generated name
    ///     when the harness is reused.
    /// </remarks>
    public string? Database { get; init; }

    /// <summary>
    ///     The name of the database created by the current or the last start, or <see langword="null" /> if the
    ///     harness has not been started.
    /// </summary>
    public string? CurrentDatabase => _database;

    /// <summary>
    ///     The template database to copy, for example one created by
    ///     <see cref="PostgreSqlHarness.CreateTemplateAsync" />. When <see langword="null" />, the database is created
    ///     empty.
    /// </summary>
    public string? Template { get; init; }

    /// <summary>
    ///     Gets the connection string for the database.
    /// </summary>
    /// <returns>
    ///     The connection string.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the harness is not started.
    /// </exception>
    public string GetConnectionString()
    {
        ThrowIfComponentNotStarted();
        return postgreSql.GetConnectionString(_database!);
    }

    /// <inheritdoc />
    public override void OnConfigure(IWebHostBuilder builder)
    {
        if (ConnectionName is null)
        {
            return;
        }

        builder.UseSetting("ConnectionStrings:" + ConnectionName, GetConnectionString());
    }

    /// <inheritdoc />
    protected override async Task OnStart(CancellationToken cancellationToken)
    {
        _database = Database ?? $"test_{Guid.NewGuid():N}";
        _created = true;
        await postgreSql.CreateDatabaseAsync(_database, Template, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task OnStop(CancellationToken cancellationToken)
    {
        if (!_created)
        {
            return;
        }

        if (postgreSql.IsStarted)
        {
            await postgreSql.DropDatabaseAsync(_database!, cancellationToken).ConfigureAwait(false);
        }

        _created = false;
    }
}
