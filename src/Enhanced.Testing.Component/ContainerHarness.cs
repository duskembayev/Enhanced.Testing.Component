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

    private static readonly TimeSpan ReuseFileLockRetryDelay = TimeSpan.FromMilliseconds(50);

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
    ///     (see <see cref="SupportsReuse" />). Starts of reusable containers of the same container type are
    ///     serialized within the process and, through a lock file in the temporary directory, across processes on
    ///     the machine, so parallel test runs attach to one container instead of creating duplicates.
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
            using var fileLock = await AcquireReuseFileLockAsync(cancellationToken).ConfigureAwait(false);
            await _container.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReuseLock.Release();
        }
    }

    private static async Task<FileStream> AcquireReuseFileLockAsync(CancellationToken cancellationToken)
    {
        var name = new string(typeof(TContainer).FullName!.Select(c => char.IsLetterOrDigit(c) || c == '.' ? c : '_').ToArray());
        var path = Path.Combine(Path.GetTempPath(), $"enhanced-testing-component-reuse-{name}.lock");

        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(ReuseFileLockRetryDelay, cancellationToken).ConfigureAwait(false);
            }
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
