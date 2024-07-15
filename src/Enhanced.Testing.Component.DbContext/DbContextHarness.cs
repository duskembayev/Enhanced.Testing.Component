using Microsoft.Extensions.DependencyInjection;

namespace Enhanced.Testing.Component.DbContext;

/// <summary>
///     A harness for operating on a DbContext.
/// </summary>
/// <typeparam name="TContext">
///     Type of the DbContext to operate on.
/// </typeparam>
public class DbContextHarness<TContext> : Harness where TContext : Microsoft.EntityFrameworkCore.DbContext
{
    /// <summary>
    ///     Ensures that the database for the context exists on start.
    /// </summary>
    public bool EnsureCreated { get; set; }

    /// <summary>
    ///     Ensures that the database for the context does not exist on stop.
    /// </summary>
    public bool EnsureDeleted { get; set; }

    /// <summary>
    ///     Executes the specified action on the DbContext.
    /// </summary>
    /// <param name="action">
    ///     The action to execute.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    public async Task ExecuteAsync(Func<TContext, Task> action, CancellationToken cancellationToken = default)
    {
        ThrowIfComponentNotStarted();

        var scope = Component.Services.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await action(context).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Executes the specified action on the DbContext.
    /// </summary>
    /// <param name="action">
    ///     The action to execute.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token.
    /// </param>
    /// <typeparam name="T">
    ///     The type of the result.
    /// </typeparam>
    /// <returns>
    ///     The result of the action.
    /// </returns>
    public async Task<T> ExecuteAsync<T>(Func<TContext, Task<T>> action, CancellationToken cancellationToken = default)
    {
        ThrowIfComponentNotStarted();

        var scope = Component.Services.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            return await action(context).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Executes the specified action on the DbContext.
    /// </summary>
    /// <param name="action">
    ///     The action to execute.
    /// </param>
    public void Execute(Action<TContext> action)
    {
        ThrowIfComponentNotStarted();

        using var serviceScope = Component.Services.CreateScope();
        using var context = serviceScope.ServiceProvider.GetRequiredService<TContext>();
        action(context);
    }

    /// <summary>
    ///     Executes the specified action on the DbContext.
    /// </summary>
    /// <param name="action">
    ///     The action to execute.
    /// </param>
    /// <typeparam name="T">
    ///     The type of the result.
    /// </typeparam>
    /// <returns>
    ///     The result of the action.
    /// </returns>
    public T Execute<T>(Func<TContext, T> action)
    {
        ThrowIfComponentNotStarted();

        using var serviceScope = Component.Services.CreateScope();
        using var context = serviceScope.ServiceProvider.GetRequiredService<TContext>();
        return action(context);
    }

    /// <inheritdoc />
    protected override async Task OnStart(CancellationToken cancellationToken)
    {
        if (!EnsureCreated)
        {
            return;
        }

        await ExecuteAsync(
            async context => await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task OnStop(CancellationToken cancellationToken)
    {
        if (!EnsureDeleted)
        {
            return;
        }

        await ExecuteAsync(
            async context => await context.Database.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }
}
