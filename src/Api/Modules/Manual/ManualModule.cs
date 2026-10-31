using Api.Infrastructure.Auth;
using Api.Infrastructure.Http;
using Api.Modules.Manual.Drafts;
using Api.Modules.Manual.Drafts.Extraction;
using Api.Modules.Manual.Mcp;
using ModelContextProtocol.Protocol;

namespace Api.Modules.Manual;

public static class ManualModule
{
    /// <summary>The MCP endpoint.</summary>
    public const string McpPath = "/api/mcp";

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
        services.AddSingleton<DraftConfirmation>();
        services.AddMcpServer(options => options.ServerInfo = new Implementation
        {
            Name = "playdex",
            Title = "Playdex",
            Version = typeof(ManualModule).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
        })
            .WithHttpTransport()
            .WithTools<DraftTools>()
            .WithResources<DraftCard>();
        return services;
    }

    public static RouteGroupBuilder MapManual(this RouteGroupBuilder api)
    {
        ManualEndpoints.MapManualEndpoints(api);
        api.MapDraftEndpoints();
        // Assistants (Claude, ChatGPT, Claude Code) log sessions through MCP with the user's Playdex app key.
        api.MapMcp(McpPath["/api".Length..]).RequireAuthorization(ApiKeyAuthentication.Policy)
            .RequireRateLimiting(RateLimiting.Mcp);
        return api;
    }
}
