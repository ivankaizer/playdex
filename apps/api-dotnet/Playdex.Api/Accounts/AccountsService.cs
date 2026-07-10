using Playdex.Api.Common;

namespace Playdex.Api.Accounts;

public sealed class AccountsService(IAccountsRepository repository)
{
    public Task<List<AccountSummaryDto>> ListAccounts(long userId, CancellationToken ct) =>
        repository.ListAccounts(userId, ct);

    public Task<SessionQueryResult> ListSessions(
        long userId, long? accountId, string? title, string? gameId,
        string? from, string? to, string sort, string direction,
        int limit, int offset, int ignoreShortMinutes, CancellationToken ct)
    {
        var parsedGameId = gameId is not null ? GameId.Parse(gameId) : (long?)null;
        var fromDate = ParseDate(from);
        var toDate = ParseDate(to);
        return repository.ListSessions(userId, accountId, title, parsedGameId, fromDate, toDate,
            sort, direction, limit, offset, ignoreShortMinutes, ct);
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
        return new AccountSessionDeleteResponseDto(deleted);
    }

    public Task<AccountStatsPeriodDto> ComputeMonthlyStats(
        long userId, long? accountId, int ignoreShortMinutes, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var from = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return repository.ComputeStats(userId, accountId, from, now, ignoreShortMinutes, ct);
    }

    public Task<AccountStatsPeriodDto> ComputeYearlyStats(
        long userId, long? accountId, int ignoreShortMinutes, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var from = new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return repository.ComputeStats(userId, accountId, from, now, ignoreShortMinutes, ct);
    }

    public Task<AccountStatsPeriodDto> ComputeStatsForRange(
        long userId, long? accountId, string? from, string? to,
        int ignoreShortMinutes, CancellationToken ct)
    {
        var fromDate = ParseDate(from);
        var toDate = ParseDate(to);
        if (!fromDate.HasValue || !toDate.HasValue)
        {
            throw Http.AppProblem.Validation("from and to are required.");
        }
        return repository.ComputeStats(userId, accountId, fromDate.Value, toDate.Value, ignoreShortMinutes, ct);
    }

    public async Task<AccountStatsResponseDto> ComputeAccountStats(
        long userId, long? accountId, int ignoreShortMinutes, CancellationToken ct)
    {
        var monthly = await ComputeMonthlyStats(userId, accountId, ignoreShortMinutes, ct);
        var yearly = await ComputeYearlyStats(userId, accountId, ignoreShortMinutes, ct);
        return new AccountStatsResponseDto(accountId, monthly, yearly);
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, null, out var date) ? date : (DateTimeOffset?)null;
}
