using System.Globalization;
using IPNetwork = System.Net.IPNetwork;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Api.Infrastructure.Http;

/// <summary>
/// Per-client request limits on the endpoints that can be brute-forced or make the server call out: sign-in, token
/// refresh, the capture API and the poster proxy. Clients are told apart by IP address. Behind Caddy, Traefik or any
/// proxy on a private network the address comes from X-Forwarded-For; proxies on public addresses aren't trusted,
/// so a client can't pick its own address by sending the header.
/// </summary>
public static class RateLimiting
{
    public const string SignIn = "sign-in";
    public const string Refresh = "refresh";
    public const string Capture = "capture";
    public const string Posters = "posters";

    private static readonly IPNetwork[] PrivateNetworks =
    [
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("::1/128"),
        IPNetwork.Parse("fc00::/7"),
    ];

    public static IServiceCollection AddPlaydexRateLimiting(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = null;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var network in PrivateNetworks)
            {
                options.KnownIPNetworks.Add(network);
            }
        });

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ProblemDetails { Status = 429, Title = "Too Many Requests", Detail = "Too many requests. Try again later." },
                    ct);
            };
            limiter.AddPolicy(SignIn, context => PerClient(context, permitsPerMinute: 10));
            limiter.AddPolicy(Refresh, context => PerClient(context, permitsPerMinute: 60));
            // The Windows app pings every 30 seconds per running game, and a session start/end now and then.
            limiter.AddPolicy(Capture, context => PerClient(context, permitsPerMinute: 120));
            limiter.AddPolicy(Posters, context => PerClient(context, permitsPerMinute: 60));
        });
        return services;
    }

    private static RateLimitPartition<string> PerClient(HttpContext context, int permitsPerMinute)
    {
        // In-process test servers have no remote address; real connections always do.
        if (context.Connection.RemoteIpAddress is not { } address)
        {
            return RateLimitPartition.GetNoLimiter("no-address");
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            address.ToString(),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    }
}
