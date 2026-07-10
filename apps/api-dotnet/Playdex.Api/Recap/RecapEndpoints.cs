using System.Globalization;
using System.Security.Claims;
using Playdex.Api.Http;

namespace Playdex.Api.Recap;

public static class RecapEndpoints
{
    public static void MapRecapEndpoints(this RouteGroupBuilder api)
    {
        var recap = api.MapGroup("/recap").RequireAuthorization();
        recap.MapGet("/years", GetYears);
        recap.MapGet("", GetRecap);
    }

    private static async Task<ApiResponse<RecapYearsResponseDto>> GetYears(
        ClaimsPrincipal principal,
        RecapService service,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        return ApiResponse<RecapYearsResponseDto>.Ok(await service.GetYears(userId, ct));
    }

    private static async Task<ApiResponse<RecapResponseDto>> GetRecap(
        ClaimsPrincipal principal,
        RecapService service,
        string? from,
        string? to,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);

        DateTimeOffset? startedAt = null;
        DateTimeOffset? endedAt = null;

        if (from is not null || to is not null)
        {
            if (from is null || to is null)
                throw AppProblem.Validation("from and to are both required.");

            if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedFrom) ||
                !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedTo))
                throw AppProblem.Validation("from and to must be valid dates.");

            if (parsedFrom > parsedTo)
                throw AppProblem.Validation("from must be before to.");

            startedAt = new DateTimeOffset(
                parsedFrom.Year, parsedFrom.Month, parsedFrom.Day, 0, 0, 0, TimeSpan.Zero);

            endedAt = new DateTimeOffset(
                parsedTo.Year, parsedTo.Month, parsedTo.Day, 0, 0, 0, TimeSpan.Zero).AddDays(1);
        }

        var result = await service.GetRecap(
            userId,
            from ?? (startedAt.HasValue ? startedAt.Value.ToString("yyyy-MM-dd") : null),
            to ?? (endedAt.HasValue ? endedAt.Value.AddDays(-1).ToString("yyyy-MM-dd") : null),
            startedAt,
            endedAt,
            ct);

        return ApiResponse<RecapResponseDto>.Ok(result);
    }

    private static long GetUserId(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue("sub");
        if (!long.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
            throw AppProblem.Unauthorized("Invalid token.");

        return userId;
    }
}
