using System.Security.Claims;
using System.Text.Json;
using Api.Infrastructure.Auth;
using Api.Infrastructure.Http;

namespace Api.Modules.Providers;

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

        providers.MapPut("/{providerId}", async (string providerId, HttpContext http) =>
        {
            var payload = await http.Request.ReadFromJsonAsync<JsonElement>();
            var service = http.RequestServices.GetRequiredService<ProvidersService>();

            IResult result;
            if (payload.ValueKind != JsonValueKind.Object)
            {
                result = TypedResults.Problem("Request body is required.", statusCode: 400);
            }
            else
            {
                result = TypedResults.Ok(await service.UpdateCredential(
                    http.User.GetUserId(),
                    ParseProviderId(providerId),
                    ParseCredentialChanges(payload),
                    http.RequestAborted));
            }
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

    private static ProviderCredentialChanges ParseCredentialChanges(JsonElement payload)
    {
        var apiKey = ReadNullableString(payload, "apiKey");
        var accountId = ReadNullableString(payload, "accountId");
        return new ProviderCredentialChanges(
            apiKey.Changed,
            apiKey.Value,
            accountId.Changed,
            accountId.Value);
    }

    private static (bool Changed, string? Value) ReadNullableString(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var property)) return (false, null);
        if (property.ValueKind == JsonValueKind.Null) return (true, null);
        if (property.ValueKind != JsonValueKind.String)
            throw AppProblemException.Validation($"{name} must be a string or null.");
        var value = property.GetString()?.Trim();
        return (true, string.IsNullOrEmpty(value) ? null : value);
    }

    private static long ParseProviderId(string value)
    {
        if (!long.TryParse(value, out var id) || id <= 0)
            throw AppProblemException.Validation("providerId must be a positive integer.");
        return id;
    }
}
