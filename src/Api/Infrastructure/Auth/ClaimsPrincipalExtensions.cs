using System.Globalization;
using System.Security.Claims;
using Api.Infrastructure.Http;

namespace Api.Infrastructure.Auth;

public static class ClaimsPrincipalExtensions
{
    public static long GetUserId(this ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue("sub");
        if (!long.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
            throw AppProblemException.Unauthorized("Invalid token.");

        return userId;
    }

    public static async Task<IResult?> AssertAdminAsync(
        this ClaimsPrincipal principal,
        AuthRepository authRepo,
        CancellationToken ct)
    {
        var user = await authRepo.FindById(principal.GetUserId(), ct);
        if (user is not null && user.IsAdmin)
            return null;

        throw AppProblemException.Forbidden("Admin access required.");
    }
}
