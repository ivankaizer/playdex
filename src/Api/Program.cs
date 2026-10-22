using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Api.Infrastructure.Auth;
using Api.Infrastructure.Common;
using Api.Infrastructure.Http;
using Playdex.Persistence;
using Playdex.Persistence.Migrations;
using Api.Modules.Accounts;
using Api.Modules.Admin;
using Api.Modules.ApiCapture;
using Api.Modules.Games;
using Api.Modules.Launcher;
using Api.Modules.Manual;
using Api.Modules.Metrics;
using Api.Modules.Notifications;
using Api.Modules.Polling;
using Api.Modules.Posters;
using Api.Modules.Privacy;
using Api.Modules.Providers;
using Api.Modules.Recap;
using Api.Modules.Settings;
using Api.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(cfg =>
{
    cfg.JsonWriterOptions = new JsonWriterOptions { Indented = false };
});

var sentrySettings = PlaydexSentrySettings.From(builder.Configuration, builder.Environment);
if (sentrySettings.IsEnabled)
{
    builder.WebHost.UseSentry(sentrySettings.ConfigureApi);
}

builder.AddServiceDefaults();

var databaseUrl = builder.Configuration["DATABASE_URL"]?.Trim()
    ?? throw new InvalidOperationException("DATABASE_URL is required.");
var corsOrigins = (builder.Configuration["CORS_ORIGINS"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Where(origin => origin != "*")
    .Distinct(StringComparer.Ordinal)
    .ToArray();

var dataSource = PostgresServices.CreateDataSource(databaseUrl);

builder.Services.AddSingleton(dataSource);
builder.Services.AddPlaydexDbContext(dataSource);
builder.Services.AddSingleton(new MigrationCatalog([
    new CurrentSchemaMigration(),
    new CredentialApiKeyLockMigration(),
    new OpenXblPlatformMigration(),
    new NotificationChannelsMigration(),
    new GameExternalIdentitiesMigration(),
    new IntegrationsMigration(),
    new PlaySessionTargetFieldsMigration(),
    new PollingTargetModelMigration(),
    new ManualTargetModelMigration(),
    new DropLegacyModelMigration(),
    new CorrectLegacyApiSessionPlatformsMigration(),
    new PcPlatformHierarchyMigration(),
    new IntegrationSessionPlatformMigration(),
    new AiPlaySessionDraftsMigration(),
    new AiGenerationHistoryMigration(),
    new UserTokenVersionMigration(),
]));
builder.Services.AddSingleton<MigrationRunner>();
builder.Services.AddScoped<EventBus>();

builder.Services
    .AddAuth(builder.Configuration)
    .AddAccounts()
    .AddSettings()
    .AddPrivacy()
    .AddGames()
    .AddManual()
    .AddApiCapture()
    .AddLauncher()
    .AddProviders()
    .AddRecap()
    .AddPlaydexMetrics()
    .AddPosters(builder.Configuration)
    .AddPolling(builder.Configuration)
    .AddNotifications(builder.Configuration)
    .AddAdmin();

builder.Services.AddPlaydexRateLimiting();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    json.SerializerOptions.PropertyNameCaseInsensitive = false;
});

builder.Services.AddCors(cfg =>
{
    cfg.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(corsOrigins)
              .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
              .AllowAnyHeader();
    });
});

builder.Services.AddOpenApi();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database")
    .AddCheck<MigrationHealthCheck>("migrations");

var app = builder.Build();
await app.Services.GetRequiredService<MigrationRunner>().RunAsync();

if (args.Contains("migrate", StringComparer.OrdinalIgnoreCase))
{
    return;
}

if (args.Contains(UserCommand.Name, StringComparer.OrdinalIgnoreCase))
{
    Environment.ExitCode = await UserCommand.Run(app.Services, args, Console.In, Console.IsInputRedirected, Console.Out);
    return;
}

app.UseForwardedHeaders();
app.MapDefaultEndpoints();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ApiEnvelopeMiddleware>();
app.UseExceptionHandler();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");
api.MapGet("", () => TypedResults.Ok("Ok"));
api.MapGet("/observability/sentry", () => TypedResults.Ok(sentrySettings.ToBrowserSettings()));

api
    .MapAuth()
    .MapAccounts()
    .MapSettings()
    .MapPrivacy()
    .MapGames()
    .MapManual()
    .MapApiCapture()
    .MapLauncher()
    .MapProviders()
    .MapRecap()
    .MapMetrics()
    .MapPosters()
    .MapAdmin();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

// The API description and its UI are for local development only; production does not publish its route map.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/swagger/json");
    app.MapGet("/swagger", () => Results.Content("""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <title>Playdex API</title>
  <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5/swagger-ui.css" />
</head>
<body style="margin:0">
  <div id="swagger-ui"></div>
  <script src="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5/swagger-ui-bundle.js"></script>
  <script>SwaggerUIBundle({ url: '/swagger/json', dom_id: '#swagger-ui' })</script>
</body>
</html>
""", "text/html"));
}

app.MapFallback("/api/{**path}", RouteNotFound);

app.Run();

static IResult RouteNotFound() => TypedResults.NotFound();

public partial class Program;
