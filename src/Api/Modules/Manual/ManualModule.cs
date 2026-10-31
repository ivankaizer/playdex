using Api.Infrastructure.Auth.OAuth;
using Api.Infrastructure.Http;
using Api.Modules.Manual.Drafts;
using Api.Modules.Manual.Drafts.Extraction;
using Api.Modules.Manual.Mcp;
using ModelContextProtocol.Protocol;

namespace Api.Modules.Manual;

public static class ManualModule
{
    /// <summary>The MCP endpoint, and the OAuth resource its access tokens are issued for.</summary>
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
        services.AddOAuthResource(new OAuthResource(McpPath, "Playdex for assistants",
        [
            new(DraftTools.Scope, "Draft play sessions from what you tell it, matched to games you’ve played or found in the catalog, " +
                "and log them to your history once you accept them. It can’t edit or delete the sessions you already have."),
        ]));
        return services;
    }

    public static RouteGroupBuilder MapManual(this RouteGroupBuilder api)
    {
        ManualEndpoints.MapManualEndpoints(api);
        api.MapDraftEndpoints();
        // Assistants (Claude, ChatGPT, Claude Code) log sessions through MCP: signed in with OAuth, or with the
        // user's Playdex app key.
        api.MapMcp(McpPath["/api".Length..]).RequireAuthorization(OAuthServer.ApiKeyOrAccessTokenPolicy)
            .RequireRateLimiting(RateLimiting.Mcp);
        return api;
    }
}
