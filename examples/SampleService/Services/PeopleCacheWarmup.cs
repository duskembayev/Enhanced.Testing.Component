using Microsoft.EntityFrameworkCore;
using SampleService.Models;

namespace SampleService.Services;

public class PeopleCacheWarmup(IServiceScopeFactory scopeFactory, PeopleCache cache, IConfiguration configuration)
    : IHostedService
{
    public const string EnabledSettingName = "PeopleCache:Enabled";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>(EnabledSettingName))
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PeopleDbContext>();
        var names = await dbContext.Persons.OrderBy(p => p.Name).Select(p => p.Name).ToListAsync(cancellationToken);
        cache.Load(names);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
