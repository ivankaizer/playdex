using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Npgsql;
using Playdex.Api.Accounts;
using Playdex.Api.Admin;
using Playdex.Api.Auth;
using Playdex.Api.Common;
using Playdex.Api.Configuration;
using Playdex.Api.Data;
using Playdex.Api.Data.Migrations;
using Playdex.Api.Games;
using Playdex.Api.Http;
using Playdex.Api.Manual;
using Playdex.Api.Metrics;
using Playdex.Api.Observability;
using Playdex.Api.Playnite;
using Playdex.Api.Polling;
using Playdex.Api.Providers;
using Playdex.Api.Recap;
using Playdex.Api.Settings;
using Playdex.Api.Telegram;

var builder = WebApplication.CreateBuilder(args);
var options = PlaydexOptions.FromConfiguration(builder.Configuration);
var dataSource = new NpgsqlDataSourceBuilder(PostgresConnectionString.Normalize(options.DatabaseUrl)).Build();
var tokenService = new AuthTokenService(options);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton(tokenService);
builder.Services.AddDbContext<PlaydexDbContext>(db => db.UseNpgsql(dataSource));
builder.Services.AddScoped<AuthRepository>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddPlaydexAuthentication(tokenService);
builder.Services.AddSingleton(new MigrationCatalog([new CurrentSchemaMigration()]));
builder.Services.AddSingleton<MigrationRunner>();

builder.Services.AddScoped<AccountsRepository>();
builder.Services.AddScoped<AccountsService>();
builder.Services.AddScoped<SettingsRepository>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<GamesRepository>();
builder.Services.AddScoped<GamesService>();
builder.Services.AddScoped<ManualRepository>();
builder.Services.AddScoped<ManualService>();
builder.Services.AddScoped<PlayniteRepository>();
builder.Services.AddScoped<PlayniteService>();
builder.Services.AddScoped<ProvidersRepository>();
builder.Services.AddScoped<ProvidersService>();
builder.Services.AddScoped<AdminRepository>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<RecapRepository>();
builder.Services.AddScoped<RecapService>();
builder.Services.AddSingleton<MetricsCollector>();
builder.Services.AddScoped<PollingRepository>();
builder.Services.AddScoped<TelegramRepository>();
builder.Services.AddHttpClient<IProviderClient, SteamClient>("SteamClient")
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<IProviderClient, PsnClient>("PsnClient")
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<IgdbClient>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHostedService<PollingBackgroundService>();
builder.Services.AddHostedService<TelegramBackgroundService>();

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
api.MapAuthEndpoints();
api.MapAccountsEndpoints();
api.MapSettingsEndpoints();
api.MapGamesEndpoints();
api.MapManualEndpoints();
api.MapPlayniteEndpoints();
api.MapProvidersEndpoints();
api.MapAdminEndpoints();
api.MapRecapEndpoints();
api.MapMetricsEndpoints();

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
