using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Admin.Infrastructure;

public sealed partial class AdminDatabaseHealthCheck(
    IDbContextFactory<PlaydexDbContext> factory,
    ILogger<AdminDatabaseHealthCheck> logger) : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var connected = await db.Database.CanConnectAsync(cancellationToken);
            if (!connected) return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Database unavailable.");
            await db.Users.AsNoTracking().Select(user => user.Id).Take(1).ToListAsync(cancellationToken);
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Database and required schema are available.");
        }
        catch (Exception exception)
        {
            LogReadinessFailure(logger, exception);
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Database schema is unavailable.");
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Admin readiness check failed")]
    private static partial void LogReadinessFailure(ILogger logger, Exception exception);
}

public sealed class AdminAuthenticationService(IDbContextFactory<PlaydexDbContext> factory)
{
    public async Task<User?> ValidateAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Email == normalized, cancellationToken);
        return user?.Password is not null && user.IsAdmin && BCrypt.Net.BCrypt.Verify(password, user.Password) ? user : null;
    }
}

public sealed class AdminOperationGuard(IDbContextFactory<PlaydexDbContext> factory)
{
    public async Task<bool> IsAdministratorAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(subject, out var userId)) return false;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Users.AsNoTracking().AnyAsync(user => user.Id == userId && user.IsAdmin, cancellationToken);
    }
}
