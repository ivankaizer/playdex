using System.Globalization;

namespace Api.Modules.Admin;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin").RequireAuthorization(AdministratorRequirement.PolicyName);

        admin.MapGet("/dashboard", ReadDashboard);

        admin.MapGet("/platforms", ListPlatforms);
        admin.MapPut("/platforms/{platform}", SetPlatformEnabled);

        admin.MapGet("/integrations", ListIntegrations);
        admin.MapPatch("/integrations/{id:long}", UpdateIntegration);

        admin.MapGet("/sessions", SearchSessions);
        admin.MapPost("/sessions/transfer", TransferSessions);

        admin.MapGet("/games", SearchGames);
        admin.MapPatch("/games/{id:long}", UpdateGame);
        admin.MapPost("/games/merge", MergeGames);

        admin.MapGet("/ai-generations", SearchGenerations);
        admin.MapGet("/ai-generations/{id:guid}", GetGeneration);
        admin.MapGet("/ai-generations/{id:guid}/image", GetGenerationImage);
        admin.MapDelete("/ai-generations/{id:guid}", DeleteGeneration);
    }

    private static async Task<IResult> ReadDashboard(
        AdminService service,
        string? query, string? platform, string? dayStart, string? dayEnd,
        CancellationToken ct) =>
        TypedResults.Ok(await service.ReadDashboard(query, platform, ParseTime(dayStart), ParseTime(dayEnd), ct));

    private static async Task<IResult> ListPlatforms(AdminService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ListPlatforms(ct));

    private static async Task<IResult> SetPlatformEnabled(
        string platform,
        AdminSetPlatformEnabledRequest? request,
        AdminService service,
        CancellationToken ct)
    {
        if (request?.Enabled is not { } enabled)
            return TypedResults.Problem("enabled is required.", statusCode: 400);
        return TypedResults.Ok(await service.SetPlatformEnabled(platform, enabled, ct));
    }

    private static async Task<IResult> ListIntegrations(AdminService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ListIntegrations(ct));

    private static async Task<IResult> UpdateIntegration(
        long id,
        AdminUpdateIntegrationRequest? request,
        AdminService service,
        CancellationToken ct)
    {
        if (request is null) return TypedResults.Problem("Request body is required.", statusCode: 400);
        await service.UpdateIntegration(id, request, ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> SearchSessions(
        AdminService service,
        string? query, int? page, string? status, string? from, string? to,
        CancellationToken ct) =>
        TypedResults.Ok(await service.SearchSessions(query, page, status, ParseTime(from), ParseTime(to), ct));

    private static async Task<IResult> TransferSessions(
        AdminTransferSessionsRequest? request,
        AdminService service,
        CancellationToken ct)
    {
        if (request is null) return TypedResults.Problem("Request body is required.", statusCode: 400);
        return TypedResults.Ok(await service.TransferSessions(request, ct));
    }

    private static async Task<IResult> SearchGames(AdminService service, string? query, CancellationToken ct) =>
        TypedResults.Ok(await service.SearchGames(query, ct));

    private static async Task<IResult> UpdateGame(
        long id,
        AdminUpdateGameRequest? request,
        AdminService service,
        CancellationToken ct)
    {
        if (request is null) return TypedResults.Problem("Request body is required.", statusCode: 400);
        await service.UpdateGame(id, request, ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> MergeGames(
        AdminMergeGamesRequest? request,
        AdminService service,
        CancellationToken ct)
    {
        if (request is null) return TypedResults.Problem("Request body is required.", statusCode: 400);
        await service.MergeGames(request, ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> SearchGenerations(
        AdminService service,
        string? query, string? status, int? page, string? since, string? until,
        CancellationToken ct) =>
        TypedResults.Ok(await service.SearchGenerations(query, status, page, ParseTime(since), ParseTime(until), ct));

    private static async Task<IResult> GetGeneration(Guid id, AdminService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetGeneration(id, ct));

    private static async Task<IResult> GetGenerationImage(
        Guid id,
        AdminService service,
        HttpContext context,
        CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        var image = await service.GetGenerationImage(id, ct);
        return TypedResults.File(image.Bytes, image.ContentType);
    }

    private static async Task<IResult> DeleteGeneration(Guid id, AdminService service, CancellationToken ct)
    {
        await service.DeleteGeneration(id, ct);
        return TypedResults.NoContent();
    }

    /// <summary>Lenient ISO-8601 parsing: an unparseable value means "no bound", as in the original admin.</summary>
    private static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : null;
}
