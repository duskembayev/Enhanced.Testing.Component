using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Enhanced.Testing.Component.WireMock;

/// <summary>
///     A WireMock harness that fakes a gRPC backend the application calls. The server listens over HTTP/2, and a
///     stub names a call by its request message type:
///     <c>backend.On&lt;HelloRequest&gt;().Respond(new HelloReply { Message = "Hello" })</c>.
/// </summary>
/// <remarks>
///     <para>
///         The harness finds the method among its services by the request type. If several methods take the same
///         request type, the type of the response tells them apart; if it does not, name the method or the service
///         in <c>On</c>.
///     </para>
///     <para>
///         When several stubs fit a call, a stub with a condition answers before a stub without one, and among
///         equal stubs the one made last answers.
///     </para>
///     <para>
///         The extensions of <see cref="WireMockGrpcExtensions" /> and <see cref="WireMockHarness.Server" /> stay
///         available for what these stubs do not cover.
///     </para>
/// </remarks>
public class WireMockGrpcHarness : WireMockHarness
{
    private const string AsyncSuffix = "Async";

    private int _stubs;

    /// <summary>
    ///     Creates a harness that fakes the services.
    /// </summary>
    /// <param name="services">
    ///     The descriptors of the services from the generated code, for example <c>Greeter.Descriptor</c>.
    /// </param>
    /// <exception cref="ArgumentException">
    ///     Thrown if no service is given or a service is given twice.
    /// </exception>
    public WireMockGrpcHarness(params ServiceDescriptor[] services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Length == 0)
        {
            throw new ArgumentException("Pass the descriptor of at least one gRPC service.", nameof(services));
        }

        foreach (var service in services)
        {
            ArgumentNullException.ThrowIfNull(service, nameof(services));
        }

        var repeated = services.GroupBy(service => service.FullName).FirstOrDefault(group => group.Count() > 1);

        if (repeated is not null)
        {
            throw new ArgumentException($"The gRPC service '{repeated.Key}' is given twice.", nameof(services));
        }

        Services = services.ToArray();
        UseHttp2 = true;
    }

    /// <summary>
    ///     The services the harness fakes.
    /// </summary>
    public IReadOnlyList<ServiceDescriptor> Services { get; }

    /// <summary>
    ///     Selects the calls of the method that takes <typeparamref name="TRequest" /> messages.
    /// </summary>
    /// <param name="predicate">
    ///     The condition the request message must satisfy; <see langword="null" /> selects every call of the
    ///     method.
    /// </param>
    /// <typeparam name="TRequest">
    ///     The request message type of the method.
    /// </typeparam>
    /// <returns>
    ///     The calls: to respond to, to fail, or to read the requests of.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if no unary method of the services takes <typeparamref name="TRequest" /> messages.
    /// </exception>
    public WireMockGrpcCall<TRequest> On<TRequest>(Func<TRequest, bool>? predicate = null)
        where TRequest : IMessage<TRequest>, new()
    {
        return Select(null, null, predicate);
    }

    /// <summary>
    ///     Selects the calls of the named method that takes <typeparamref name="TRequest" /> messages. Use it when
    ///     several methods take the same request type.
    /// </summary>
    /// <param name="method">
    ///     The method name, as in the <c>.proto</c> file or as the client names it (with the <c>Async</c> suffix),
    ///     for example <c>nameof(Greeter.GreeterClient.SayHelloAsync)</c>. The full name, such as
    ///     <c>greet.Greeter/SayHello</c>, is accepted too.
    /// </param>
    /// <param name="predicate">
    ///     The condition the request message must satisfy; <see langword="null" /> selects every call of the
    ///     method.
    /// </param>
    /// <typeparam name="TRequest">
    ///     The request message type of the method.
    /// </typeparam>
    /// <returns>
    ///     The calls: to respond to, to fail, or to read the requests of.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if no unary method of the services has this name and takes <typeparamref name="TRequest" />
    ///     messages.
    /// </exception>
    public WireMockGrpcCall<TRequest> On<TRequest>(string method, Func<TRequest, bool>? predicate = null)
        where TRequest : IMessage<TRequest>, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        return Select(null, method, predicate);
    }

    /// <summary>
    ///     Selects the calls of the method of the service that takes <typeparamref name="TRequest" /> messages. Use
    ///     it when methods of several services take the same request type.
    /// </summary>
    /// <param name="service">
    ///     The descriptor of the service, one of those the harness was created with.
    /// </param>
    /// <param name="predicate">
    ///     The condition the request message must satisfy; <see langword="null" /> selects every call of the
    ///     method.
    /// </param>
    /// <typeparam name="TRequest">
    ///     The request message type of the method.
    /// </typeparam>
    /// <returns>
    ///     The calls: to respond to, to fail, or to read the requests of.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if the service is not one of the harness, or none of its unary methods takes
    ///     <typeparamref name="TRequest" /> messages.
    /// </exception>
    public WireMockGrpcCall<TRequest> On<TRequest>(ServiceDescriptor service, Func<TRequest, bool>? predicate = null)
        where TRequest : IMessage<TRequest>, new()
    {
        ArgumentNullException.ThrowIfNull(service);

        return Select(service, null, predicate);
    }

    /// <summary>
    ///     Selects the calls of the named method of the service.
    /// </summary>
    /// <param name="service">
    ///     The descriptor of the service, one of those the harness was created with.
    /// </param>
    /// <param name="method">
    ///     The method name, as in the <c>.proto</c> file or as the client names it (with the <c>Async</c> suffix),
    ///     for example <c>nameof(Greeter.GreeterClient.SayHelloAsync)</c>. The full name, such as
    ///     <c>greet.Greeter/SayHello</c>, is accepted too.
    /// </param>
    /// <param name="predicate">
    ///     The condition the request message must satisfy; <see langword="null" /> selects every call of the
    ///     method.
    /// </param>
    /// <typeparam name="TRequest">
    ///     The request message type of the method.
    /// </typeparam>
    /// <returns>
    ///     The calls: to respond to, to fail, or to read the requests of.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if the service is not one of the harness, or it has no unary method with this name that takes
    ///     <typeparamref name="TRequest" /> messages.
    /// </exception>
    public WireMockGrpcCall<TRequest> On<TRequest>(ServiceDescriptor service, string method,
        Func<TRequest, bool>? predicate = null)
        where TRequest : IMessage<TRequest>, new()
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        return Select(service, method, predicate);
    }

    internal int NextStub() => Interlocked.Increment(ref _stubs);

    private WireMockGrpcCall<TRequest> Select<TRequest>(ServiceDescriptor? service, string? method,
        Func<TRequest, bool>? predicate)
        where TRequest : IMessage<TRequest>, new()
    {
        if (service is not null && !Services.Any(known => known.FullName == service.FullName))
        {
            throw new ArgumentException(
                $"The gRPC service '{service.FullName}' is not one of this harness. Its services: " +
                $"{string.Join(", ", Services.Select(known => known.FullName))}.", nameof(service));
        }

        var taking = Services.SelectMany(known => known.Methods)
                             .Where(candidate => candidate.InputType.ClrType == typeof(TRequest))
                             .Where(candidate => method is null || Named(candidate, method))
                             .ToList();

        var candidates = taking.Where(candidate => service is null || candidate.Service.FullName == service.FullName)
                               .Where(Unary)
                               .ToList();

        if (candidates.Count > 0)
        {
            return new WireMockGrpcCall<TRequest>(this, candidates, predicate);
        }

        var unary = Services.Where(known => service is null || known.FullName == service.FullName)
                            .SelectMany(known => known.Methods)
                            .Where(Unary)
                            .ToList();

        var streaming = taking.Where(candidate => !Unary(candidate)).ToList();
        var elsewhere = taking.Where(Unary).ToList();

        throw new ArgumentException(
            $"No unary gRPC method{(method is null ? "" : $" '{method}'")}" +
            $"{(service is null ? "" : $" of the service '{service.FullName}'")} takes {typeof(TRequest).Name} " +
            "messages." +
            (streaming.Count == 0
                ? ""
                : $" Streaming methods take them, and the harness does not stub those: " +
                  $"{WireMockGrpcCall<TRequest>.Describe(streaming)}.") +
            (elsewhere.Count == 0
                ? ""
                : $" Other services have such a method: {WireMockGrpcCall<TRequest>.Describe(elsewhere)}.") +
            $" The unary methods{(service is null ? "" : " of the service")}: " +
            $"{WireMockGrpcCall<TRequest>.Describe(unary)}.");
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the server was set not to listen over HTTP/2.
    /// </exception>
    protected override async Task OnStart(CancellationToken cancellationToken)
    {
        await base.OnStart(cancellationToken).ConfigureAwait(false);

        if (ListensOverHttp2)
        {
            return;
        }

        await OnStop(cancellationToken).ConfigureAwait(false);

        throw new InvalidOperationException(
            $"A gRPC harness must listen over HTTP/2: do not set {nameof(UseHttp2)} to false, on the harness or in " +
            $"{nameof(ConfigureServer)}.");
    }

    private static bool Unary(MethodDescriptor method) => !method.IsClientStreaming && !method.IsServerStreaming;

    private static bool Named(MethodDescriptor candidate, string method)
    {
        method = method.TrimStart('/');

        var slash = method.LastIndexOf('/');

        if (slash >= 0)
        {
            if (candidate.Service.FullName != method[..slash])
            {
                return false;
            }

            method = method[(slash + 1)..];
        }

        return candidate.Name == method ||
               (method.EndsWith(AsyncSuffix, StringComparison.Ordinal) &&
                candidate.Name == method[..^AsyncSuffix.Length]);
    }
}
