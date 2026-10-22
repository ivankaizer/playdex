using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Api.Tests;

/// <summary>
/// Hosts the API for integration tests without the hosted services the API registers itself.
/// Those workers run on timers against the shared integration database: in Development the poller
/// runs every 10 seconds, ends every API session without a ping in the last 3 minutes and polls every
/// active integration. They would change rows while a test is reading them, so tests that cover a
/// worker call it directly instead.
/// </summary>
public sealed class PlaydexApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            var apiAssembly = typeof(Program).Assembly;
            var workers = services
                .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                    && !descriptor.IsKeyedService
                    && descriptor.ImplementationType?.Assembly == apiAssembly)
                .ToList();
            foreach (var worker in workers)
            {
                services.Remove(worker);
            }
        });
}
