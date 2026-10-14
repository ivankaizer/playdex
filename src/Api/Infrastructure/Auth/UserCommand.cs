using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Api.Infrastructure.Auth;

/// <summary>
/// <c>dotnet Api.dll create-user &lt;email&gt; [--admin]</c>: creates an account, or sets a new password on an
/// existing one. Playdex has no sign-up, so this is how a fresh server gets its first admin. The password is
/// read from stdin when one is piped in; otherwise one is generated and printed once.
/// </summary>
public static class UserCommand
{
    public const string Name = "create-user";

    public sealed record Result(bool Created, string Password);

    public static async Task<int> Run(IServiceProvider services, string[] args, TextReader input, bool inputRedirected, TextWriter output)
    {
        var rest = args.SkipWhile(arg => !arg.Equals(Name, StringComparison.OrdinalIgnoreCase)).Skip(1).ToArray();
        var email = rest.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal))?.Trim();
        var isAdmin = rest.Contains("--admin", StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
        {
            output.WriteLine($"usage: {Name} <email> [--admin]   (pipe a password in, or one is generated)");
            return 2;
        }

        var piped = inputRedirected ? (await input.ReadLineAsync())?.Trim() : null;
        var password = string.IsNullOrEmpty(piped) ? null : piped;

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlaydexDbContext>();
        var result = await Upsert(db, email, isAdmin, password, DateTimeOffset.UtcNow, CancellationToken.None);

        output.WriteLine($"{(result.Created ? "Created" : "Updated")} {email}{(isAdmin ? " (admin)" : "")}.");
        if (password is null)
        {
            output.WriteLine($"Password: {result.Password}");
        }
        return 0;
    }

    /// <summary>Creates the user or replaces its password. <c>isAdmin</c> only ever grants admin, never takes it away.</summary>
    public static async Task<Result> Upsert(
        PlaydexDbContext db, string email, bool isAdmin, string? password, DateTimeOffset now, CancellationToken ct)
    {
        password ??= GeneratePassword();
        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        var user = await db.Users.SingleOrDefaultAsync(row => row.Email == email, ct);
        var created = user is null;
        if (user is null)
        {
            user = new User { Email = email, CreatedAt = now };
            db.Users.Add(user);
        }

        user.Password = hash;
        user.IsAdmin |= isAdmin;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return new Result(created, password);
    }

    private static string GeneratePassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_');
}
