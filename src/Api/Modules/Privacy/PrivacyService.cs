using Api.Infrastructure.Http;

namespace Api.Modules.Privacy;

public sealed class PrivacyService(PrivacyRepository repository, TimeProvider clock)
{
    public async Task<AccountExportDto> Export(long userId, CancellationToken ct)
    {
        var user = await repository.FindUser(userId, ct)
            ?? throw AppProblemException.NotFound("User not found.");
        return await repository.Export(user, clock.GetUtcNow(), ct);
    }

    /// <summary>Deletes the account after re-checking the password, so a stolen token alone cannot erase it.</summary>
    public async Task DeleteAccount(long userId, string? password, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(password))
            throw AppProblemException.Validation("Password is required to delete your account.");

        var user = await repository.FindUser(userId, ct)
            ?? throw AppProblemException.NotFound("User not found.");
        if (user.Password is null || !BCrypt.Net.BCrypt.Verify(password, user.Password))
            throw AppProblemException.Forbidden("Password is incorrect.");

        await repository.DeleteUser(userId, ct);
    }
}
