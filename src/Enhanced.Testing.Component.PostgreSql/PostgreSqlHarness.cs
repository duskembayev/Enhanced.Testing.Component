using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace Enhanced.Testing.Component.PostgreSql;

/// <summary>
///     A PostgreSQL harness.
/// </summary>
public class PostgreSqlHarness : Harness
{
    private PostgreSqlContainer? _container;

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
    ///     The name of the connection string to add to the configuration.
    /// </summary>
    public string? ConnectionStringName { get; init; }

    /// <summary>
    ///     The connection string properties.
    /// </summary>
    public IDictionary<string, string> ConnectionStringProperties { get; init; } = new Dictionary<string, string>();

    /// <summary>
    ///     The PostgreSQL container.
    /// </summary>
    public PostgreSqlContainer Container
    {
        get
        {
            ThrowIfComponentNotStarted();
            return _container!;
        }
    }

    /// <summary>
    ///     Gets the connection string for the PostgreSQL database.
    /// </summary>
    /// <returns>
    ///     The connection string.
    /// </returns>
    public string GetConnectionString()
    {
        var properties = new Dictionary<string, string>(ConnectionStringProperties)
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
    public override void OnConfigure(IWebHostBuilder webHostBuilder)
    {
        if (ConnectionStringName is null)
        {
            return;
        }

        webHostBuilder.ConfigureAppConfiguration(
            builder => builder.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:" + ConnectionStringName] = GetConnectionString()
                }));
    }

    /// <inheritdoc />
    protected override async Task OnStart(CancellationToken cancellationToken)
    {
        _container = new PostgreSqlBuilder()
                     .WithUsername(Username)
                     .WithPassword(Password)
                     .WithDatabase(Database)
                     .Build();

        await _container.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task OnStop(CancellationToken cancellationToken)
    {
        if (_container is not null)
        {
            await _container.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
