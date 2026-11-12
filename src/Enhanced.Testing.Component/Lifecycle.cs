using System.Runtime.ExceptionServices;

namespace Enhanced.Testing.Component;

internal static class Lifecycle
{
    public static async Task<bool> StopInReverseAsync<T>(
        List<T> items,
        Func<T, Task> stop,
        List<Exception> exceptions,
        CancellationToken cancellationToken)
    {
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return true;
            }

            try
            {
                await stop(items[i]).ConfigureAwait(false);
                items.RemoveAt(i);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return true;
            }
            catch (Exception e)
            {
                exceptions.Add(e);
            }
        }

        return false;
    }

    public static void ThrowIfFailed(
        List<Exception> exceptions,
        bool cancelled,
        string message,
        CancellationToken cancellationToken)
    {
        if (exceptions.Count == 1)
        {
            ExceptionDispatchInfo.Throw(exceptions[0]);
        }

        if (exceptions.Count > 1)
        {
            throw new AggregateException(message, exceptions);
        }

        if (cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    public static async Task WhenAllAsync(List<Task> tasks, string message, CancellationToken cancellationToken)
    {
        List<Exception> exceptions = [];

        foreach (var task in tasks)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception e)
            {
                exceptions.Add(e);
            }
        }

        if (exceptions.Count == 0)
        {
            return;
        }

        var failures = exceptions
                       .Where(e => e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                       .ToList();

        if (failures.Count == 0)
        {
            ExceptionDispatchInfo.Throw(exceptions[0]);
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Throw(failures[0]);
        }

        throw new AggregateException(message, failures);
    }

    public static async Task RollBackAsync(Exception startException, Func<Task> stop, string message)
    {
        try
        {
            await stop().ConfigureAwait(false);
        }
        catch (Exception stopException)
        {
            throw new AggregateException(message, startException, stopException);
        }
    }
}
