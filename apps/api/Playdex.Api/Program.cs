using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Npgsql;
using Playdex.Api.Shared.Auth;
using Playdex.Api.Shared.Common;
using Playdex.Api.Shared.Http;
using Playdex.Api.Shared.Persistence;
using Playdex.Api.Shared.Persistence.Migrations;
using Playdex.Api.Modules.Accounts;
using Playdex.Api.Modules.Admin;
using Playdex.Api.Modules.Games;
using Playdex.Api.Modules.Manual;
using Playdex.Api.Modules.Metrics;
using Playdex.Api.Modules.Playnite;
using Playdex.Api.Modules.Polling;
using Playdex.Api.Modules.Providers;
using Playdex.Api.Modules.Recap;
using Playdex.Api.Modules.Settings;
using Playdex.Api.Modules.Telegram;
using Playdex.Api.Observability;

var builder = WebApplication.CreateBuilder(args);
var options = PlaydexOptions.FromConfiguration(builder.Configuration);
var dataSource = new NpgsqlDataSourceBuilder(PostgresConnectionString.Normalize(options.DatabaseUrl)).Build();
var tokenService = new AuthTokenService(options);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton(tokenService);
builder.Services.AddDbContext<PlaydexDbContext>(db => db.UseNpgsql(dataSource));
builder.Services.AddSingleton(new MigrationCatalog([new CurrentSchemaMigration()]));
builder.Services.AddSingleton<MigrationRunner>();
builder.Services.AddScoped<EventBus>();

builder.Services
    .AddAuth(tokenService)
    .AddAccounts()
    .AddSettings()
    .AddGames()
    .AddManual()
    .AddPlaynite()
    .AddProviders()
    .AddAdmin()
    .AddRecap()
    .AddPlaydexMetrics()
    .AddPolling()
    .AddTelegram();

builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    json.SerializerOptions.PropertyNameCaseInsensitive = false;
});

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(cfg =>
{
    cfg.JsonWriterOptions = new JsonWriterOptions { Indented = false };
});

builder.Services.AddCors(cfg =>
{
    cfg.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins([..options.CorsOrigins])
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

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ApiEnvelopeMiddleware>();
app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");
api.MapGet("", () => TypedResults.Ok("Ok"));

api
    .MapAuth()
    .MapAccounts()
    .MapSettings()
    .MapGames()
    .MapManual()
    .MapPlaynite()
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
