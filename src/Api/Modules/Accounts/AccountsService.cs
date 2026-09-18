using Api.Infrastructure.Http;
using Api.Infrastructure.Common;
using Api.Modules.Games;
using Playdex.Persistence;

namespace Api.Modules.Accounts;

public sealed class AccountsService(AccountsRepository repository)
{
    internal static AccountSessionUpdateRequest ValidateSessionUpdate(
        PlaySession? session,
        long userId,
        AccountSessionUpdateRequest request)
    {
        if (session is null || session.UserId != userId)
        {
            throw AppProblemException.NotFound("Session not found.");
        }

        if (session.EndedAt is null)
        {
            throw AppProblemException.Validation("Only finished sessions can be edited.");
        }

        if (request.StartedAt == default || request.EndedAt == default)
        {
            throw AppProblemException.Validation("Start and end time are required.");
        }

        if (request.EndedAt <= request.StartedAt)
        {
            throw AppProblemException.Validation("End time must be later than start time.");
        }

        return request;
    }

    internal static AccountSessionUpdateResponseDto ApplySessionUpdate(
        PlaySession? session,
        long userId,
        AccountSessionUpdateRequest request,
        DateTimeOffset updatedAt)
    {
        var validated = ValidateSessionUpdate(session, userId, request);
        var editableSession = session!;
        editableSession.StartedAt = validated.StartedAt.ToUniversalTime();
        editableSession.EndedAt = validated.EndedAt.ToUniversalTime();
        editableSession.UpdatedAt = updatedAt;
        return new AccountSessionUpdateResponseDto(
            editableSession.Id,
            editableSession.StartedAt,
            editableSession.EndedAt.Value);
    }

    public Task<List<AccountSummaryDto>> ListAccounts(long userId, CancellationToken ct) =>
        repository.ListAccounts(userId, ct);

    public async Task<SessionQueryResult> ListSessions(
        long userId, long? accountId, string? title, string? gameId,
        string? platform, string? from, string? to, string sort, string direction,
        int limit, int offset, int ignoreShortMinutes, CancellationToken ct)
    {
        IReadOnlyCollection<long>? gameIds = null;
        if (gameId is not null)
        {
            var parsedGameId = GameId.Parse(gameId);
            gameIds = await repository.ResolveConnectedGameIds(userId, parsedGameId, ct);
        }
        var range = AccountDateRange.ParseOptional(from, to);
        return await repository.ListSessions(userId, accountId, title, gameIds, platform, range.From, range.ToExclusive,
            sort, direction, limit, offset, ignoreShortMinutes, ct);
    }

    public async Task<AccountSessionUpdateResponseDto> UpdateSession(
        long sessionId,
        long userId,
        AccountSessionUpdateRequest request,
        CancellationToken ct)
    {
        var session = await repository.GetSession(sessionId, ct);
        var response = ApplySessionUpdate(session, userId, request, DateTimeOffset.UtcNow);
        await repository.SaveChanges(ct);
        return response;
    }

    public Task<List<AccountSessionDto>> ListSessionsToday(
        long userId, long? accountId, int ignoreShortMinutes, CancellationToken ct) =>
        repository.ListSessionsToday(userId, accountId, ignoreShortMinutes, ct);

    public Task<AccountSessionDto?> LoadCurrentSession(
        long userId, long? accountId, CancellationToken ct) =>
        repository.LoadCurrentSession(userId, accountId, ct);

    public async Task<AccountSessionDeleteResponseDto> DeleteSession(
        long sessionId, long userId, CancellationToken ct)
    {
        var deleted = await repository.DeleteSession(sessionId, userId, ct);
        if (deleted == 0)
        {
            throw AppProblemException.NotFound("Session not found.");
        }
        return new AccountSessionDeleteResponseDto(deleted);
    }

    public Task<AccountStatsPeriodDto> ComputeMonthlyStats(
        long userId, long? accountId, int ignoreShortMinutes, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var from = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return repository.ComputeStats(userId, accountId, from, now, null, ignoreShortMinutes, ct);
    }

    public Task<AccountStatsPeriodDto> ComputeYearlyStats(
        long userId, long? accountId, int ignoreShortMinutes, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var from = new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return repository.ComputeStats(userId, accountId, from, now, null, ignoreShortMinutes, ct);
    }

    public Task<AccountStatsPeriodDto> ComputeStatsForRange(
        long userId, long? accountId, string? from, string? to, string? platform,
        int ignoreShortMinutes, CancellationToken ct)
    {
        var range = AccountDateRange.ParseRequired(from, to);
        return repository.ComputeStats(
            userId, accountId, range.From!.Value, range.ToExclusive!.Value, platform, ignoreShortMinutes, ct);
    }

    public async Task<AccountStatsResponseDto> ComputeAccountStats(
        long userId, long? accountId, int ignoreShortMinutes, CancellationToken ct)
    {
        var monthly = await ComputeMonthlyStats(userId, accountId, ignoreShortMinutes, ct);
        var yearly = await ComputeYearlyStats(userId, accountId, ignoreShortMinutes, ct);
        return new AccountStatsResponseDto(accountId, monthly, yearly);
    }

    public async Task<GameStatsResponseDto> ComputeGameStats(
        long userId,
        string gameId,
        CancellationToken ct)
    {
        var parsedGameId = GameId.Parse(gameId);
        var gameIds = await repository.ResolveConnectedGameIds(userId, parsedGameId, ct);
        return await repository.ComputeGameStats(userId, gameIds, ct);
    }
}
