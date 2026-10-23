using System.Globalization;
using Microsoft.AspNetCore.Authorization;

namespace Api.Modules.Admin;

/// <summary>
/// Requires the caller to be an administrator according to the database at request time.
/// The JWT carries no role claim, so a demoted administrator loses access immediately.
/// </summary>
public sealed class AdministratorRequirement : IAuthorizationRequirement
{
    public const string PolicyName = "Administrator";
}

public sealed class AdministratorRequirementHandler(AdminRepository repository)
    : AuthorizationHandler<AdministratorRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdministratorRequirement requirement)
    {
        var subject = context.User.FindFirst("sub")?.Value;
        if (!long.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
        {
            return;
        }

        var ct = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        if (await repository.IsAdministrator(userId, ct))
        {
            context.Succeed(requirement);
        }
    }
}
