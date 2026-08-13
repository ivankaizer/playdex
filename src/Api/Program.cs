using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Npgsql;
using Api.Infrastructure.Auth;
using Api.Infrastructure.Common;
using Api.Infrastructure.Http;
using Api.Infrastructure.Persistence;
using Api.Infrastructure.Persistence.Migrations;
using Api.Modules.Accounts;
using Api.Modules.Admin;
using Api.Modules.ApiCapture;
using Api.Modules.Games;
using Api.Modules.Manual;
using Api.Modules.Metrics;
using Api.Modules.Notifications;
using Api.Modules.Polling;
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

var dataSourceBuilder = new NpgsqlDataSourceBuilder(PostgresConnectionString.Normalize(databaseUrl));
dataSourceBuilder.MapEnum<GameExternalIdentityType>("game_external_identity_type");
dataSourceBuilder.MapEnum<IntegrationType>("integration_type");
dataSourceBuilder.MapEnum<CaptureMethod>("capture_method");
var dataSource = dataSourceBuilder.Build();

builder.Services.AddSingleton(dataSource);
builder.Services.AddDbContext<PlaydexDbContext>(db => db.UseNpgsql(
    dataSource,
    npgsql =>
    {
        npgsql.MapEnum<GameExternalIdentityType>("game_external_identity_type");
        npgsql.MapEnum<IntegrationType>("integration_type");
        npgsql.MapEnum<CaptureMethod>("capture_method");
    }));
builder.Services.AddSingleton(new MigrationCatalog([
    new CurrentSchemaMigration(),
    new CredentialApiKeyLockMigration(),
    new OpenXblPlatformMigration(),
    new GameExternalIdentitiesMigration(),
    new IntegrationsMigration(),
    new PlaySessionTargetFieldsMigration(),
    new PollingTargetModelMigration(),
    new ManualTargetModelMigration(),
    new DropLegacyModelMigration(),
]));
builder.Services.AddSingleton<MigrationRunner>();
builder.Services.AddScoped<EventBus>();

builder.Services
    .AddAuth(builder.Configuration)
    .AddAccounts()
    .AddSettings()
    .AddGames()
    .AddManual()
    .AddApiCapture()
    .AddProviders()
    .AddAdmin()
    .AddRecap()
    .AddPlaydexMetrics()
    .AddPolling(builder.Configuration)
    .AddNotifications(builder.Configuration);

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
              .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
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

app.MapDefaultEndpoints();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ApiEnvelopeMiddleware>();
app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");
api.MapGet("", () => TypedResults.Ok("Ok"));
api.MapGet("/observability/sentry", () => TypedResults.Ok(sentrySettings.ToBrowserSettings()));

api
    .MapAuth()
    .MapAccounts()
    .MapSettings()
    .MapGames()
    .MapManual()
    .MapApiCapture()
    .MapProviders()
    .MapAdmin()
    .MapRecap()
    .MapMetrics();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

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

app.MapFallback("/api/{**path}", RouteNotFound);

app.Run();

static IResult RouteNotFound() => TypedResults.NotFound();

public partial class Program;
