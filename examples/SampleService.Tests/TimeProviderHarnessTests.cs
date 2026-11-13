using System.Net.Http.Json;
using Enhanced.Testing.Component;
using Enhanced.Testing.Component.Time;
using Microsoft.Extensions.DependencyInjection;

namespace SampleService.Tests;

public class TimeProviderHarnessTests : IAsyncLifetime
{
    private static readonly DateTimeOffset StartTime = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly HttpClientHarness _httpClient = new();
    private readonly TimeProviderHarness _time = new(StartTime);
    private readonly IComponent _component;

    public TimeProviderHarnessTests()
    {
        _component = ComponentBuilder.Create<Program>()
                                     .AddHarness(_httpClient)
                                     .AddHarness(_time)
                                     .Build();
    }

    [Fact]
    public void ShouldReplaceApplicationTimeProvider()
    {
        var timeProviders = _component.Services.GetServices<TimeProvider>().ToArray();

        Assert.Same(_time.TimeProvider, Assert.Single(timeProviders));
    }

    [Fact]
    public async Task ShouldReturnInitialTime()
    {
        var now = await _httpClient.CreateClient().GetFromJsonAsync<DateTimeOffset>("/time");

        Assert.Equal(StartTime, now);
        Assert.Equal(StartTime, _time.UtcNow);
    }

    [Fact]
    public async Task ShouldReturnAdvancedTime()
    {
        _time.Advance(TimeSpan.FromHours(5));
        var advanced = await _httpClient.CreateClient().GetFromJsonAsync<DateTimeOffset>("/time");

        var target = StartTime.AddDays(10);
        _time.SetUtcNow(target);
        var set = await _httpClient.CreateClient().GetFromJsonAsync<DateTimeOffset>("/time");

        Assert.Equal(StartTime.AddHours(5), advanced);
        Assert.Equal(target, set);
    }

    [Fact]
    public async Task ShouldCompleteDelayWhenTimeAdvances()
    {
        var response = _httpClient.CreateClient().GetFromJsonAsync<DateTimeOffset>("/delay/3600");
        var timeout = DateTime.UtcNow.AddSeconds(30);

        while (!response.IsCompleted && DateTime.UtcNow < timeout)
        {
            _time.Advance(TimeSpan.FromMinutes(10));
            await Task.WhenAny(response, Task.Delay(TimeSpan.FromMilliseconds(20)));
        }

        Assert.True(response.IsCompleted, "The delayed request did not complete after advancing the time.");
        var completedAt = await response;

        Assert.True(completedAt >= StartTime.AddHours(1));
    }

    [Fact]
    public void ShouldUseDefaultStartTime()
    {
        var harness = new TimeProviderHarness();

        Assert.Equal(harness.TimeProvider.Start, harness.UtcNow);
    }

    Task IAsyncLifetime.InitializeAsync() => _component.StartAsync(default);

    Task IAsyncLifetime.DisposeAsync() => _component.StopAsync(default);
}
