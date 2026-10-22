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
        AccountSessionUpdateRequest request,
        DateTimeOffset? now = null)
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

        if (request.EndedAt - request.StartedAt > SessionLimits.MaxDuration)
        {
            throw AppProblemException.Validation("A session can last at most 24 hours.");
        }

        if (request.EndedAt > (now ?? DateTimeOffset.UtcNow) + SessionLimits.ClockSkew)
        {
            throw AppProblemException.Validation("A session cannot end in the future.");
        }

        return request;
    }

    internal static AccountSessionUpdateResponseDto ApplySessionUpdate(
        PlaySession? session,
        long userId,
        AccountSessionUpdateRequest request,
        DateTimeOffset updatedAt)
    {
        var validated = ValidateSessionUpdate(session, userId, request, updatedAt);
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
        int limit, int offset, int ignoreShortMinutes, TimeZoneInfo zone, CancellationToken ct)
    {
        IReadOnlyCollection<long>? gameIds = null;
        if (gameId is not null)
        {
            var parsedGameId = GameId.Parse(gameId);
            gameIds = await repository.ResolveConnectedGameIds(userId, parsedGameId, ct);
        }
        var range = AccountDateRange.ParseOptional(from, to, zone);
        return await repository.ListSessions(userId, accountId, SearchText.OptionalFilter(title, "title"), gameIds, platform, range.From, range.ToExclusive,
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
        long userId, long? accountId, int ignoreShortMinutes, TimeZoneInfo zone, CancellationToken ct) =>
        repository.ListSessionsToday(userId, accountId, ignoreShortMinutes, zone, ct);

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
        long userId, long? accountId, int ignoreShortMinutes, TimeZoneInfo zone, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var today = LocalCalendar.DateOf(now, zone);
        var from = LocalCalendar.StartOf(new DateOnly(today.Year, today.Month, 1), zone);
        return repository.ComputeStats(userId, accountId, from, now, null, null, ignoreShortMinutes, zone, ct);
    }

    public Task<AccountStatsPeriodDto> ComputeYearlyStats(
        long userId, long? accountId, int ignoreShortMinutes, TimeZoneInfo zone, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var from = LocalCalendar.StartOf(new DateOnly(LocalCalendar.DateOf(now, zone).Year, 1, 1), zone);
        return repository.ComputeStats(userId, accountId, from, now, null, null, ignoreShortMinutes, zone, ct);
    }

    /// <summary>Stats of the sessions started in the range; <paramref name="title"/> filters like the session list.</summary>
    public Task<AccountStatsPeriodDto> ComputeStatsForRange(
        long userId, long? accountId, string? from, string? to, string? platform, string? title,
        int ignoreShortMinutes, TimeZoneInfo zone, CancellationToken ct)
    {
        var range = AccountDateRange.ParseRequired(from, to, zone);
        return repository.ComputeStats(
            userId, accountId, range.From!.Value, range.ToExclusive!.Value, platform, SearchText.OptionalFilter(title, "title"), ignoreShortMinutes, zone, ct);
    }

    /// <summary>Longest range the daily breakdown serves, so one request stays small.</summary>
    public const int MaxDailyStatsDays = 366;

    public Task<List<AccountDayStatDto>> ComputeDailyStats(
        long userId, long? accountId, string? from, string? to, string? platform, string? title,
        int ignoreShortMinutes, TimeZoneInfo zone, CancellationToken ct)
    {
        var range = AccountDateRange.ParseRequired(from, to, zone);
        var dayCount = LocalCalendar.DateOf(range.ToExclusive!.Value, zone).DayNumber
            - LocalCalendar.DateOf(range.From!.Value, zone).DayNumber;
        if (dayCount > MaxDailyStatsDays)
            throw AppProblemException.Validation($"Daily stats cover at most {MaxDailyStatsDays} days.");

        return repository.ComputeDailyStats(
            userId, accountId, range.From.Value, range.ToExclusive.Value, platform, SearchText.OptionalFilter(title, "title"), ignoreShortMinutes, zone, ct);
    }

    public async Task<AccountStatsResponseDto> ComputeAccountStats(
        long userId, long? accountId, int ignoreShortMinutes, TimeZoneInfo zone, CancellationToken ct)
    {
        var monthly = await ComputeMonthlyStats(userId, accountId, ignoreShortMinutes, zone, ct);
        var yearly = await ComputeYearlyStats(userId, accountId, ignoreShortMinutes, zone, ct);
        return new AccountStatsResponseDto(accountId, monthly, yearly);
    }

    public async Task<GameStatsResponseDto> ComputeGameStats(
        long userId,
        string gameId,
        TimeZoneInfo zone,
        CancellationToken ct)
    {
        var parsedGameId = GameId.Parse(gameId);
        var gameIds = await repository.ResolveConnectedGameIds(userId, parsedGameId, ct);
        return await repository.ComputeGameStats(userId, gameIds, zone, ct);
    }
}
