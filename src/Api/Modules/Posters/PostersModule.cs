using Microsoft.Extensions.Options;

namespace Api.Modules.Posters;

public static class PostersModule
{
    public static IServiceCollection AddPosters(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PostersOptions>(configuration.GetSection("Posters"));
        services.AddSingleton<PosterUrls>();
        services.AddSingleton<IPosterStore, R2PosterStore>();
        services.AddSingleton<PosterService>();
        services.AddHttpClient(PosterService.WeModHttpClient, c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHostedService<PosterUrlBackfill>();
        return services;
    }

    public static RouteGroupBuilder MapPosters(this RouteGroupBuilder api)
    {
        api.MapGet("/posters/steam/{appId:long:min(0)}", async (long appId, HttpContext http) =>
        {
            if (!http.RequestServices.GetRequiredService<IOptions<PostersOptions>>().Value.IsEnabled)
            {
                return Results.NotFound();
            }

            var poster = await http.RequestServices.GetRequiredService<PosterService>()
                .GetSteamCapsule(appId, http.RequestAborted);
            if (poster is null)
            {
                return Results.NotFound();
            }

            http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.File(poster.Content, poster.ContentType);
        });
        return api;
    }
}
