using System.Collections.Concurrent;
using Grpc.Core;
using Grpc.Net.Client;

namespace Enhanced.Testing.Component.GrpcClient;

/// <summary>
///     A harness for creating gRPC clients.
/// </summary>
public class GrpcClientHarness : Harness
{
    private readonly ConcurrentDictionary<Type, Lazy<GrpcChannel>> _channels = new();

    /// <summary>
    ///     Creates a gRPC client that calls the application under test.
    /// </summary>
    /// <typeparam name="TClient">
    ///     Type of the gRPC client to create.
    /// </typeparam>
    /// <returns>
    ///     Instance of the gRPC client.
    /// </returns>
    public TClient CreateClient<TClient>()
        where TClient : ClientBase
    {
        ThrowIfComponentNotStarted();
        var lazyChannel = _channels.GetOrAdd(typeof(TClient), _ => new Lazy<GrpcChannel>(CreateGrpcChannel));
        GrpcChannel channel;

        try
        {
            channel = lazyChannel.Value;
        }
        catch
        {
            _channels.TryRemove(new KeyValuePair<Type, Lazy<GrpcChannel>>(typeof(TClient), lazyChannel));
            throw;
        }

        return (TClient)Activator.CreateInstance(typeof(TClient), channel)!;
    }

    private GrpcChannel CreateGrpcChannel()
    {
        var httpClient = Component.CreateClient();
        return GrpcChannel.ForAddress(
            httpClient.BaseAddress!,
            new GrpcChannelOptions { HttpClient = httpClient, DisposeHttpClient = true });
    }

    /// <inheritdoc />
    protected override async Task OnStop(CancellationToken cancellationToken)
    {
        foreach (var type in _channels.Keys)
        {
            if (!_channels.TryRemove(type, out var lazyChannel) || !lazyChannel.IsValueCreated)
            {
                continue;
            }

            var channel = lazyChannel.Value;

            try
            {
                await channel.ShutdownAsync().ConfigureAwait(false);
            }
            finally
            {
                channel.Dispose();
            }
        }
    }
}
