using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Enhanced.Testing.Component.Time;

/// <summary>
///     A harness that replaces the application's <see cref="System.TimeProvider" /> with a <see cref="FakeTimeProvider" />.
/// </summary>
public class TimeProviderHarness : Harness
{
    /// <summary>
    ///     Creates a harness whose clock starts at the <see cref="FakeTimeProvider" /> default time.
    /// </summary>
    public TimeProviderHarness()
        : this(new FakeTimeProvider())
    {
    }

    /// <summary>
    ///     Creates a harness whose clock starts at the specified time.
    /// </summary>
    /// <param name="startDateTime">
    ///     The initial time returned by <see cref="System.TimeProvider.GetUtcNow" />.
    /// </param>
    public TimeProviderHarness(DateTimeOffset startDateTime)
        : this(new FakeTimeProvider(startDateTime))
    {
    }

    /// <summary>
    ///     Creates a harness that uses the specified fake time provider.
    /// </summary>
    /// <param name="timeProvider">
    ///     The fake time provider to register in the application.
    /// </param>
    public TimeProviderHarness(FakeTimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        TimeProvider = timeProvider;
    }

    /// <summary>
    ///     The fake time provider registered as the application's <see cref="System.TimeProvider" />.
    /// </summary>
    public FakeTimeProvider TimeProvider { get; }

    /// <summary>
    ///     Gets the current fake time.
    /// </summary>
    public DateTimeOffset UtcNow => TimeProvider.GetUtcNow();

    /// <summary>
    ///     Advances the fake time and fires the timers that become due.
    /// </summary>
    /// <param name="delta">
    ///     The amount of time to advance.
    /// </param>
    public void Advance(TimeSpan delta) => TimeProvider.Advance(delta);

    /// <summary>
    ///     Sets the fake time and fires the timers that become due.
    /// </summary>
    /// <param name="value">
    ///     The new time, which must not be earlier than the current fake time.
    /// </param>
    public void SetUtcNow(DateTimeOffset value) => TimeProvider.SetUtcNow(value);

    /// <inheritdoc />
    public override void OnConfigure(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(TimeProvider);
        });
}
