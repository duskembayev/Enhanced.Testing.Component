namespace Enhanced.Testing.Component;

/// <summary>
///     The base class for a harness that can be shared by an <see cref="IComponentEnvironment" /> as well as used by a
///     single component, such as a container or an in-process fake server.
/// </summary>
/// <remarks>
///     A harness instance is started by one component or environment at a time. When it is started by an environment,
///     <see cref="Harness.Component" /> is not available.
/// </remarks>
public abstract class EnvironmentHarness : Harness, IEnvironmentHarness
{
    Task IEnvironmentHarness.OnStartAsync(IComponentEnvironment environment, CancellationToken cancellationToken) =>
        AttachAsync(environment, null, cancellationToken);

    Task IEnvironmentHarness.OnStopAsync(IComponentEnvironment environment, CancellationToken cancellationToken) =>
        DetachAsync(environment, cancellationToken);
}
