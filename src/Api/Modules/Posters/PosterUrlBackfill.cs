using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Playdex.Persistence;

namespace Api.Modules.Posters;

// Points games that still reference WeMod directly at this API's poster endpoint.
public sealed partial class PosterUrlBackfill(
    IServiceScopeFactory scopeFactory,
    IOptions<PostersOptions> options,
    ILogger<PosterUrlBackfill> logger) : IHostedService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Moved {Count} game posters from WeMod to the poster endpoint")]
    private partial void LogBackfilled(int count);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.IsEnabled)
        {
            return;
        }

        var baseUrl = $"{options.Value.PublicBaseUrl!.TrimEnd('/')}/api/posters/steam/";
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlaydexDbContext>();
        var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
            update games
            set capsule_url = {baseUrl} || substring(capsule_url from '^https://api-cdn\.wemod\.com/steam_apps/(\d+)/hero_capsule\.webp$'),
                updated_at = now()
            where capsule_url ~ '^https://api-cdn\.wemod\.com/steam_apps/\d+/hero_capsule\.webp$'
            """, cancellationToken);
        if (count > 0)
        {
            LogBackfilled(count);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
