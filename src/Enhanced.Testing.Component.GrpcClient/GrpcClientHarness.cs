using System.Collections.Concurrent;
using Grpc.Core;
using Grpc.Net.Client;

namespace Enhanced.Testing.Component.GrpcClient;

/// <summary>
///     A harness for creating gRPC clients.
/// </summary>
public class GrpcClientHarness : Harness
{
    private readonly ConcurrentDictionary<Type, GrpcChannel> _channels = new();

    /// <summary>
    ///     Creates a new instance of gRPC client />.
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
        var channel = _channels.GetOrAdd(typeof(TClient), _ => CreateGrpcChannel());
        return (TClient)Activator.CreateInstance(typeof(TClient), channel)!;
    }

    private GrpcChannel CreateGrpcChannel()
    {
        var httpClient = Component.CreateClient();
        return GrpcChannel.ForAddress(
            httpClient.BaseAddress!,
            new GrpcChannelOptions { HttpClient = httpClient });
    }

    /// <inheritdoc />
    protected override async Task OnStop(CancellationToken cancellationToken)
    {
        foreach (var channel in _channels.Values)
        {
            await channel.ShutdownAsync().ConfigureAwait(false);
            channel.Dispose();
        }

        _channels.Clear();
    }
}
