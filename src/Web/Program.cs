using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using Sentry;
using Web;
using Web.Infrastructure.Auth;
using Web.Infrastructure.Browser;
using Web.Infrastructure.Http;
using Web.Observability;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
var configuredApiBase = builder.Configuration["ApiBaseUrl"]?.Trim();
var apiBase = string.IsNullOrEmpty(configuredApiBase) || configuredApiBase == "__PLAYDEX_API_URL__"
    ? new Uri(new Uri(builder.HostEnvironment.BaseAddress), "/api/").AbsoluteUri
    : configuredApiBase;

var sentrySettings = await BrowserSentrySettings.ResolveAsync(
    builder.Configuration,
    builder.HostEnvironment.BaseAddress,
    apiBase,
    builder.HostEnvironment.Environment);
if (sentrySettings.IsEnabled)
{
    builder.UseSentry(options =>
    {
        options.Dsn = sentrySettings.Dsn;
        options.Environment = sentrySettings.Environment;
        options.Release = sentrySettings.Release;
        options.TracesSampleRate = sentrySettings.TracesSampleRate;
        options.EnableLogs = true;
        options.MinimumBreadcrumbLevel = LogLevel.Information;
        options.MinimumEventLevel = LogLevel.Error;
        options.SendDefaultPii = false;
        options.DefaultTags["component"] = "web";
        options.AddExceptionFilterForType<OperationCanceledException>();
    });
}

builder.RootComponents.Add<App>("#app");
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
});
builder.Services.AddScoped<IBrowserStorage, BrowserStorage>();
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<BrowserInterop>();
builder.Services.AddScoped<PlaydexApiClient>(services => new PlaydexApiClient(
    services.GetRequiredService<HttpClient>(),
    services.GetRequiredService<AuthSession>(),
    apiBase));

try
{
    await builder.Build().RunAsync();
}
catch (Exception exception)
{
    SentrySdk.CaptureException(exception);
    await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));
    throw;
}
