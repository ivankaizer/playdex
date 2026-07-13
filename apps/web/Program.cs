using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Playdex.Web;
using Playdex.Web.Api;
using Playdex.Web.Auth;
using Playdex.Web.Browser;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
});
builder.Services.AddScoped<IBrowserStorage, BrowserStorage>();
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<BrowserInterop>();

var configuredApiBase = builder.Configuration["ApiBaseUrl"]?.Trim();
var apiBase = string.IsNullOrEmpty(configuredApiBase) || configuredApiBase == "__PLAYDEX_API_URL__"
    ? new Uri(new Uri(builder.HostEnvironment.BaseAddress), "/api/").AbsoluteUri
    : configuredApiBase;
builder.Services.AddScoped<PlaydexApiClient>(services => new PlaydexApiClient(
    services.GetRequiredService<HttpClient>(),
    services.GetRequiredService<AuthSession>(),
    apiBase));

await builder.Build().RunAsync();
