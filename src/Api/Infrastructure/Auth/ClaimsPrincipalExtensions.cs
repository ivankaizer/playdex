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
        {
            throw AppProblemException.Unauthorized("Invalid token.");
        }

        return userId;
    }
}
