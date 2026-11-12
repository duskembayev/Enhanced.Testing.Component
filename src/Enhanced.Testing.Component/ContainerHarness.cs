using DotNet.Testcontainers.Containers;

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
    private static readonly SemaphoreSlim ReuseLock = new(1, 1);

    private TContainer? _container;

    /// <summary>
    ///     The container.
    /// </summary>
    public TContainer Container
    {
        get
        {
            ThrowIfComponentNotStarted();

            if (_container is null)
            {
                throw new InvalidOperationException("The container is not running.");
            }

            return _container;
        }
    }

    /// <summary>
    ///     The name of the connection string to add to the configuration.
    /// </summary>
    public string? ConnectionName { get; init; }

    /// <summary>
    ///     Reuses a running container with the same configuration and keeps the container running when the harness
    ///     stops. Intended for local development only. Ignored by harnesses that don't support reuse
    ///     (see <see cref="SupportsReuse" />).
    /// </summary>
    public bool Reuse { get; init; }

    /// <summary>
    ///     Whether <see cref="OnCreateContainer" /> passes <see cref="Reuse" /> to the container builder
    ///     (<c>WithReuse</c>).
    /// </summary>
    protected virtual bool SupportsReuse => false;

    private bool IsReused => Reuse && SupportsReuse;

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

        if (!IsReused)
        {
            await _container.StartAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await ReuseLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _container.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReuseLock.Release();
        }
    }

    /// <inheritdoc />
    protected override async Task OnStop(CancellationToken cancellationToken)
    {
        if (_container is null)
        {
            return;
        }

        if (!IsReused)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }

        _container = default;
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
