using Api.Modules.Manual.Drafts;
using Api.Modules.Manual.Drafts.Extraction;

namespace Api.Modules.Manual;

public static class ManualModule
{
    public static IServiceCollection AddManual(this IServiceCollection services)
    {
        services.AddScoped<ManualRepository>();
        services.AddScoped<ManualService>();
        services.AddScoped<DraftRepository>();
        services.AddScoped<DraftService>();
        services.AddScoped<DraftExtractionService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<GenerationHistoryRecorder>();
        services.AddHostedService<GenerationHistoryCleanup>();
        services.AddOptions<AiDraftOptions>().BindConfiguration("AiDrafts");
        services.AddHttpClient<IPlaySessionExtractor, OpenAiPlaySessionExtractor>(client => client.Timeout = Timeout.InfiniteTimeSpan);
        return services;
    }

    public static RouteGroupBuilder MapManual(this RouteGroupBuilder api)
    {
        ManualEndpoints.MapManualEndpoints(api);
        api.MapDraftEndpoints();
        return api;
    }
}
