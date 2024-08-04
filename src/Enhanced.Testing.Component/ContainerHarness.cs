using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;

namespace Enhanced.Testing.Component;

/// <summary>
///     An abstract container harness.
/// </summary>
/// <typeparam name="TContainer">
///     The type of the container.
/// </typeparam>
public abstract class ContainerHarness<TContainer> : Harness
    where TContainer : IContainer
{
    private TContainer? _container;

    /// <summary>
    ///     The container.
    /// </summary>
    public TContainer Container
    {
        get
        {
            ThrowIfComponentNotStarted();
            return _container!;
        }
    }

    /// <summary>
    ///     The name of the connection string to add to the configuration.
    /// </summary>
    public string? ConnectionName { get; init; }

    /// <inheritdoc />
    public override void OnConfigure(IWebHostBuilder webHostBuilder)
    {
        if (ConnectionName is null)
        {
            return;
        }

        webHostBuilder.UseSetting("ConnectionStrings:" + ConnectionName, GetConnectionString());
    }

    /// <inheritdoc />
    protected override async Task OnStart(CancellationToken cancellationToken)
    {
        _container = OnCreateContainer();
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

    /// <summary>
    ///     Gets the connection string for the container.
    /// </summary>
    /// <returns>
    ///     The connection string.
    /// </returns>
    public abstract string? GetConnectionString();

    /// <summary>
    ///     Creates the container.
    /// </summary>
    /// <returns>
    ///     The container.
    /// </returns>
    protected abstract TContainer OnCreateContainer();
}
