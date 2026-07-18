using System.Security.Claims;
using System.Text.Json;
using Playdex.Api.Auth;
using Playdex.Api.Common;

namespace Playdex.Api.Admin;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin").RequireAuthorization();

        admin.MapGet("/sessions/recent", GetRecentSessions);
        admin.MapGet("/sessions", SearchSessions);

        admin.MapPut("/sessions/move", async (HttpContext http) =>
        {
            var request = await http.Request.ReadFromJsonAsync<AdminSessionMoveRequest>();
            var authRepo = http.RequestServices.GetRequiredService<AuthRepository>();
            var service = http.RequestServices.GetRequiredService<AdminService>();

            IResult result;
            if (request is null)
            {
                result = TypedResults.Problem("Request body is required.", statusCode: 400);
            }
            else
            {
                var forbid = await http.User.AssertAdminAsync(authRepo, http.RequestAborted);
                result = forbid ?? TypedResults.Ok(await service.MoveSessions(request, http.RequestAborted));
            }
            await result.ExecuteAsync(http);
        });

        admin.MapGet("/providers/platforms", GetPlatformStatuses);
        admin.MapPut("/providers/platforms/status", async (HttpContext http) =>
        {
            var request = await http.Request.ReadFromJsonAsync<AdminPlatformStatusUpdateRequest>();
            var authRepo = http.RequestServices.GetRequiredService<AuthRepository>();
            var service = http.RequestServices.GetRequiredService<AdminService>();

            IResult result;
            if (request is null)
            {
                result = TypedResults.Problem("Request body is required.", statusCode: 400);
            }
            else
            {
                var forbid = await http.User.AssertAdminAsync(authRepo, http.RequestAborted);
                result = forbid ?? TypedResults.Ok(await service.UpdatePlatformStatus(request, http.RequestAborted));
            }
            await result.ExecuteAsync(http);
        });

        admin.MapGet("/credentials", ListCredentials);
        admin.MapPut("/credentials/{credentialId:long}", async (long credentialId, HttpContext http) =>
        {
            var payload = await http.Request.ReadFromJsonAsync<JsonElement>();
            var authRepo = http.RequestServices.GetRequiredService<AuthRepository>();
            var service = http.RequestServices.GetRequiredService<AdminService>();

            IResult result;
            if (payload.ValueKind != JsonValueKind.Object)
            {
                result = TypedResults.Problem("Request body is required.", statusCode: 400);
            }
            else
            {
                var forbid = await http.User.AssertAdminAsync(authRepo, http.RequestAborted);
                result = forbid ?? TypedResults.Ok(await service.UpdateCredential(
                    credentialId,
                    ParseCredentialChanges(payload),
                    http.RequestAborted));
            }
            await result.ExecuteAsync(http);
        });

        admin.MapGet("/games/search", SearchGames);
        admin.MapPut("/games", (HttpContext http) => UpdateGame(null, http));
        admin.MapPut("/games/{gameId}", (string gameId, HttpContext http) => UpdateGame(gameId, http));
    }

    private static async Task<IResult> GetRecentSessions(
        ClaimsPrincipal principal,
        AuthRepository authRepo,
        AdminService service,
        CancellationToken ct)
    {
        var forbid = await principal.AssertAdminAsync(authRepo, ct);
        if (forbid is not null) return forbid;

        return TypedResults.Ok(await service.GetRecentSessions(ct));
    }

    private static async Task<IResult> SearchSessions(
        ClaimsPrincipal principal,
        AuthRepository authRepo,
        AdminService service,
        string? query, int? limit, int? offset,
        CancellationToken ct)
    {
        var forbid = await principal.AssertAdminAsync(authRepo, ct);
        if (forbid is not null) return forbid;

        return TypedResults.Ok(await service.SearchSessions(
            query ?? "", Math.Clamp(limit ?? 100, 1, 500), Math.Max(offset ?? 0, 0), ct));
    }

    private static async Task<IResult> GetPlatformStatuses(
        ClaimsPrincipal principal,
        AuthRepository authRepo,
        AdminService service,
        CancellationToken ct)
    {
        var forbid = await principal.AssertAdminAsync(authRepo, ct);
        if (forbid is not null) return forbid;

        return TypedResults.Ok(await service.GetPlatformStatuses(ct));
    }

    private static async Task<IResult> ListCredentials(
        ClaimsPrincipal principal,
        AuthRepository authRepo,
        AdminService service,
        CancellationToken ct)
    {
        var forbid = await principal.AssertAdminAsync(authRepo, ct);
        if (forbid is not null) return forbid;

        return TypedResults.Ok(await service.ListCredentials(ct));
    }

    private static async Task<IResult> SearchGames(
        ClaimsPrincipal principal,
        AuthRepository authRepo,
        AdminService service,
        string? query, int? limit,
        CancellationToken ct)
    {
        var forbid = await principal.AssertAdminAsync(authRepo, ct);
        if (forbid is not null) return forbid;

        return TypedResults.Ok(await service.SearchGames(query ?? "", limit ?? 20, ct));
    }

    private static async Task UpdateGame(string? routeGameId, HttpContext http)
    {
        var payload = await http.Request.ReadFromJsonAsync<JsonElement>();
        IResult result;
        if (payload.ValueKind != JsonValueKind.Object)
        {
            result = TypedResults.Problem("Request body is required.", statusCode: 400);
        }
        else
        {
            var authRepo = http.RequestServices.GetRequiredService<AuthRepository>();
            var service = http.RequestServices.GetRequiredService<AdminService>();
            var forbid = await http.User.AssertAdminAsync(authRepo, http.RequestAborted);
            if (forbid is not null)
            {
                result = forbid;
            }
            else
            {
                var rawGameId = routeGameId
                    ?? ReadIdentifier(payload, "gameId")
                    ?? ReadIdentifier(payload, "id")
                    ?? http.Request.Query["gameId"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(rawGameId))
                    throw Http.AppProblem.Validation("gameId is required.");
                result = TypedResults.Ok(await service.UpdateGame(
                    GameId.Parse(rawGameId, "gameId"),
                    ParseGameChanges(payload),
                    http.RequestAborted));
            }
        }

        await result.ExecuteAsync(http);
    }

    internal static AdminCredentialChanges ParseCredentialChanges(JsonElement payload)
    {
        var apiKey = ReadNullableString(payload, "apiKey");
        var accountUser = ReadNullableString(payload, "user");
        return new AdminCredentialChanges(
            apiKey.Changed,
            apiKey.Value,
            accountUser.Changed,
            accountUser.Value);
    }

    internal static AdminGameChanges ParseGameChanges(JsonElement payload)
    {
        var title = ReadRequiredString(payload, "title");
        var imageUrl = ReadNullableString(payload, "imageUrl");
        var lockTitle = ReadBoolean(payload, "lockTitle");
        var lockImage = ReadBoolean(payload, "lockImage");
        return new AdminGameChanges(
            title.Changed,
            title.Value ?? string.Empty,
            imageUrl.Changed,
            imageUrl.Value,
            lockTitle.Changed,
            lockTitle.Value,
            lockImage.Changed,
            lockImage.Value);
    }

    private static (bool Changed, string? Value) ReadNullableString(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var property)) return (false, null);
        if (property.ValueKind == JsonValueKind.Null) return (true, null);
        if (property.ValueKind != JsonValueKind.String)
            throw Http.AppProblem.Validation($"{name} must be a string or null.");
        var value = property.GetString()?.Trim();
        return (true, string.IsNullOrEmpty(value) ? null : value);
    }

    private static (bool Changed, string? Value) ReadRequiredString(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var property)) return (false, null);
        if (property.ValueKind != JsonValueKind.String)
            throw Http.AppProblem.Validation($"{name} must be a string.");
        var value = property.GetString()?.Trim();
        if (string.IsNullOrEmpty(value))
            throw Http.AppProblem.Validation($"{name} cannot be empty.");
        return (true, value);
    }

    private static (bool Changed, bool Value) ReadBoolean(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var property)) return (false, false);
        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw Http.AppProblem.Validation($"{name} must be a boolean.");
        return (true, property.GetBoolean());
    }

    private static string? ReadIdentifier(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var property)) return null;
        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => throw Http.AppProblem.Validation($"{name} must be a valid game id."),
        };
    }
}
