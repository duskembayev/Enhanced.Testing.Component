using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Enhanced.Testing.Component;

/// <summary>
///     Provides extension methods for <see cref="IComponentBuilder" />.
/// </summary>
public static class ComponentBuilderExtensions
{
    /// <summary>
    ///     Adds a harness to the component builder.
    /// </summary>
    /// <param name="componentBuilder">
    ///     The component builder.
    /// </param>
    /// <param name="harness">
    ///     The harness.
    /// </param>
    /// <returns>
    ///     The component builder.
    /// </returns>
    public static IComponentBuilder AddHarness(this IComponentBuilder componentBuilder, IHarness harness) =>
        componentBuilder.Configure(harness.OnConfigure)
                        .AddDependency(harness);

    /// <summary>
    ///     Removes the hosted service of type <typeparamref name="TService" /> so it does not run during the test.
    ///     Registrations by type, instance and factory made by the application, or by builder configuration added
    ///     before this call, are removed.
    /// </summary>
    /// <typeparam name="TService">
    ///     The exact implementation type of the hosted service.
    /// </typeparam>
    /// <param name="componentBuilder">
    ///     The component builder.
    /// </param>
    /// <returns>
    ///     The component builder.
    /// </returns>
    public static IComponentBuilder RemoveHostedService<TService>(this IComponentBuilder componentBuilder)
        where TService : class, IHostedService =>
        componentBuilder.Configure(builder => builder.ConfigureTestServices(services =>
        {
            for (var i = services.Count - 1; i >= 0; i--)
            {
                if (IsHostedService<TService>(services[i]))
                {
                    services.RemoveAt(i);
                }
            }
        }));

    private static bool IsHostedService<TService>(ServiceDescriptor descriptor)
    {
        if (descriptor.ServiceType != typeof(IHostedService) || descriptor.IsKeyedService)
        {
            return false;
        }

        var implementationType = descriptor.ImplementationType
                                 ?? descriptor.ImplementationInstance?.GetType()
                                 ?? descriptor.ImplementationFactory?.GetType().GenericTypeArguments[^1];

        return implementationType == typeof(TService);
    }
}
