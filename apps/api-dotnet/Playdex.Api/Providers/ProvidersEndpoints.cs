using System.Security.Claims;
using Playdex.Api.Auth;

namespace Playdex.Api.Providers;

public static class ProvidersEndpoints
{
    public static void MapProvidersEndpoints(this RouteGroupBuilder api)
    {
        var providers = api.MapGroup("/providers").RequireAuthorization();
        providers.MapGet("", ListProviders);

        providers.MapPut("/status", async (HttpContext http) =>
        {
            var request = await http.Request.ReadFromJsonAsync<ProviderUpdateRequest>();
            var service = http.RequestServices.GetRequiredService<ProvidersService>();
            IResult result = request is null
                ? TypedResults.Problem("Request body is required.", statusCode: 400)
                : TypedResults.Ok(await service.UpdateProviderStatus(http.User.GetUserId(), request, http.RequestAborted));
            await result.ExecuteAsync(http);
        });

        api.MapPost("/playnite/key", GetPlayniteKey).RequireAuthorization();
    }

    private static async Task<IResult> ListProviders(
        ClaimsPrincipal principal,
        ProvidersService service,
        CancellationToken ct)
    {
        return TypedResults.Ok(await service.ListProviders(principal.GetUserId(), ct));
    }

    private static async Task<IResult> GetPlayniteKey(
        ClaimsPrincipal principal,
        ProvidersService service,
        CancellationToken ct)
    {
        return TypedResults.Ok(await service.GetOrCreatePlayniteKey(principal.GetUserId(), ct));
    }
}
