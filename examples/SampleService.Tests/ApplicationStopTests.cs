using System.Diagnostics;
using Enhanced.Testing.Component;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SampleService.Tests;

public class ApplicationStopTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ShouldReportFailedBackgroundServiceWhenApplicationStops()
    {
        var failure = new InvalidOperationException("The outbox is broken.");
        var trigger = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var httpClient = new HttpClientHarness();
        var component = ComponentBuilder.Create<Program>()
                                        .Configure(builder => builder.ConfigureServices(services =>
                                            services.AddHostedService(_ => new FailingService(trigger.Task, failure))))
                                        .AddHarness(httpClient)
                                        .Build();

        try
        {
            await component.StartAsync(default);
            Assert.NotNull(component.Services);

            trigger.SetResult();

            var exception = await WaitForFailureAsync(() => component.Services);

            Assert.Contains(nameof(FailingService), exception.Message);
            Assert.Contains(failure.Message, exception.Message);
            Assert.Same(failure, exception.InnerException);
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => httpClient.CreateClient()).InnerException);
        }
        finally
        {
            await component.StopAsync(default);
        }

        var stopped = Assert.Throws<InvalidOperationException>(() => component.Services);
        Assert.Equal("The component has been stopped.", stopped.Message);
    }

    [Fact]
    public async Task ShouldReportApplicationThatStopsItself()
    {
        var component = ComponentBuilder.Create<Program>().Build();

        try
        {
            await component.StartAsync(default);

            component.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();

            var exception = await WaitForFailureAsync(() => component.Services);

            Assert.Contains("the component did not stop it", exception.Message);
            Assert.Null(exception.InnerException);
        }
        finally
        {
            await component.StopAsync(default);
        }
    }

    [Fact]
    public async Task ShouldNotReportBackgroundServiceFailureThatIsIgnored()
    {
        var trigger = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("The outbox is broken.");
        var component = ComponentBuilder.Create<Program>()
                                        .Configure(builder => builder.ConfigureServices(services =>
                                        {
                                            services.Configure<HostOptions>(options =>
                                                options.BackgroundServiceExceptionBehavior =
                                                    BackgroundServiceExceptionBehavior.Ignore);
                                            services.AddHostedService(_ =>
                                                new FailingService(trigger.Task, failure, failed));
                                        }))
                                        .Build();

        try
        {
            await component.StartAsync(default);

            trigger.SetResult();
            await failed.Task.WaitAsync(Timeout);

            Assert.NotNull(component.Services);
        }
        finally
        {
            await component.StopAsync(default);
        }
    }

    private static async Task<InvalidOperationException> WaitForFailureAsync(Func<object> access)
    {
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            var exception = Record.Exception(access);

            if (exception is not null)
            {
                return Assert.IsType<InvalidOperationException>(exception);
            }

            Assert.True(stopwatch.Elapsed < Timeout, "The component did not report that the application stopped.");
            await Task.Delay(20);
        }
    }

    private sealed class FailingService(Task trigger, Exception failure, TaskCompletionSource? failed = null)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await trigger.WaitAsync(stoppingToken);
            failed?.TrySetResult();
            throw failure;
        }
    }
}
