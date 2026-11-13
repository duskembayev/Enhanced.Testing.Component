using Microsoft.AspNetCore.Hosting;

namespace Enhanced.Testing.Component.PostgreSql;

/// <summary>
///     A database of its own for a component, created in a shared PostgreSQL container when the component starts and
///     dropped when it stops. Use it with a <see cref="PostgreSqlHarness" /> shared by a
///     <see cref="IComponentEnvironment" /> or by a test fixture, so every component gets clean data without starting
///     a container.
/// </summary>
/// <param name="postgreSql">
///     The PostgreSQL harness that runs the container. It must be started before this harness: share it through an
///     environment, or add it to the same component before this harness.
/// </param>
public class PostgreSqlDatabaseHarness(PostgreSqlHarness postgreSql) : Harness
{
    private bool _created;

    /// <summary>
    ///     The name of the connection string to add to the configuration of the component. It overrides a connection
    ///     string with the same name set by the <see cref="PostgreSqlHarness" />.
    /// </summary>
    public string? ConnectionName { get; init; }

    /// <summary>
    ///     The database name. By default a unique name is generated for the harness instance.
    /// </summary>
    public string Database { get; init; } = $"test_{Guid.NewGuid():N}";

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
        return postgreSql.GetConnectionString(Database);
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
        await postgreSql.CreateDatabaseAsync(Database, Template, cancellationToken).ConfigureAwait(false);
        _created = true;
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
            await postgreSql.DropDatabaseAsync(Database, cancellationToken).ConfigureAwait(false);
        }

        _created = false;
    }
}
