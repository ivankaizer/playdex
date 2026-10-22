using System.Text.Json;
using Api.Infrastructure.Common;
using Api.Infrastructure.Http;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Api.Modules.Manual.Drafts;

public sealed class DraftService(DraftRepository repository, PlaydexDbContext db,
    ManualService manual, ManualRepository manualRepository)
{
    public async Task<DraftBatchDto> Get(long userId, Guid id, CancellationToken ct) =>
        ToDto(await RequireBatch(userId, id, ct));

    public async Task<IReadOnlyList<DraftBatchDto>> List(long userId, CancellationToken ct) =>
        (await repository.ListBatches(userId, ct)).Select(ToDto).ToArray();

    public async Task<DraftRowDto> Update(long userId, Guid batchId, Guid rowId, DraftUpdateRequest request, CancellationToken ct)
    {
        var batch = await RequireBatch(userId, batchId, ct);
        var existing = batch.Rows.SingleOrDefault(r => r.Id == rowId) ?? throw AppProblemException.NotFound();
        RequirePending(existing, request.Revision);
        var title = request.Title?.Trim() ?? "";
        if (title.Length > 500) throw AppProblemException.Validation("Game title must be at most 500 characters.");
        if (request.Date?.Length > 10) throw AppProblemException.Validation("Use a calendar date in yyyy-MM-dd format.");
        var platform = request.PlatformKey?.Trim().ToLowerInvariant() ?? "unknown";
        if (await manualRepository.GetPlatformIdByKey(platform, ct) is null)
            throw AppProblemException.Validation("Select a platform from the catalog.");
        ManualMappingSearchResultDto? selected = null;
        // A changed query invalidates the prior selection, even when a client sends its stale ID.
        if (title == existing.Title && (!string.IsNullOrWhiteSpace(request.GameId) || !string.IsNullOrWhiteSpace(request.IgdbId)))
            selected = await VerifySelection(request.GameId, request.IgdbId, ct);
        return await repository.WithLockedRow(userId, batchId, rowId, row =>
        {
            RequirePending(row, request.Revision);
            row.Title = title;
            row.Date = string.IsNullOrWhiteSpace(request.Date) ? null : request.Date;
            row.DurationMinutes = request.DurationMinutes;
            row.PlatformKey = platform;
            row.GameId = selected?.GameId;
            row.IgdbId = selected?.IgdbId;
            row.SelectionJson = selected is null ? null : JsonSerializer.Serialize(selected);
            row.Warnings = [];
            row.Revision++;
            return Task.FromResult(ToDto(row, Zone(batch), DateTimeOffset.UtcNow));
        }, ct);
    }

    public Task Delete(long userId, Guid batchId, Guid rowId, int revision, CancellationToken ct) =>
        repository.WithLockedRow(userId, batchId, rowId, row =>
        {
            RequirePending(row, revision);
            row.Status = "deleted";
            row.Revision++;
            return Task.FromResult(true);
        }, ct);

    public async Task<DraftAcceptResponse> Accept(long userId, Guid batchId, DraftAcceptRequest request, CancellationToken ct)
    {
        await RequireBatch(userId, batchId, ct);
        if (request.Rows is null || request.Rows.Count is < 1 or > 50 || request.Rows.Select(r => r.Id).Distinct().Count() != request.Rows.Count)
            throw AppProblemException.Validation("Select between 1 and 50 different draft rows.");
        var results = new List<DraftAcceptResult>();
        foreach (var requested in request.Rows)
        {
            try
            {
                var batch = await RequireBatch(userId, batchId, ct);
                var snapshot = batch.Rows.SingleOrDefault(r => r.Id == requested.Id) ?? throw AppProblemException.NotFound();
                if (snapshot.Status == "accepted")
                {
                    results.Add(new(snapshot.Id, snapshot.AcceptedSessionId, null));
                    continue;
                }
                RequirePending(snapshot, requested.Revision);
                if (!DraftRules.TryGetTimes(
                        snapshot.Date, snapshot.DurationMinutes, Zone(batch), DateTimeOffset.UtcNow, out var start, out var end))
                    throw AppProblemException.Validation("Correct the date and duration before accepting.");
                var selection = ManualService.ValidateGameSelection(snapshot.GameId, snapshot.IgdbId);
                // IGDB fetches happen before a database transaction, then revision is checked under lock.
                var authoritative = await manual.ResolveAuthoritativeSelection(selection, ct);
                var sessionId = await repository.WithLockedRow(userId, batchId, requested.Id, async row =>
                {
                    if (row.Status == "accepted") return row.AcceptedSessionId!.Value;
                    RequirePending(row, requested.Revision);
                    if (authoritative.GameId.HasValue)
                        ManualService.RequireExistingGame(await manualRepository.GetGameEntity(authoritative.GameId.Value, ct));
                    var platformId = await manualRepository.GetPlatformIdByKey(row.PlatformKey, ct)
                        ?? throw AppProblemException.Validation("Platform not found.");
                    var gameId = await manual.ResolveGame(authoritative, ct);
                    var id = await manualRepository.CreatePlaySession(userId, gameId, platformId, start, end, ct);
                    row.Status = "accepted";
                    row.AcceptedSessionId = id;
                    row.Revision++;
                    return id;
                }, ct);
                results.Add(new(requested.Id, sessionId, null));
            }
            catch (AppProblemException ex) { results.Add(new(requested.Id, null, ex.Message)); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { results.Add(new(requested.Id, null, "This draft could not be accepted. Retry after checking its game selection.")); }
        }
        return new(results);
    }

    public async Task<IReadOnlyList<ManualMappingSearchResultDto>> SearchCandidates(
        long userId,
        string title,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title)) return [];
        var previouslyPlayed = await manualRepository.SearchRecentlyPlayedGames(userId, title, 5, ct);
        var response = await manual.SearchMapping(title, ct);
        var results = previouslyPlayed.Select(PreviouslyPlayed).ToList();
        foreach (var candidate in response.Results)
        {
            var selected = candidate;
            if (candidate.Source == "igdb")
            {
                var game = await (from identity in db.GameExternalIdentities.AsNoTracking()
                    join g in db.Games.AsNoTracking() on identity.GameId equals g.Id
                    where identity.Type == GameExternalIdentityType.Igdb && identity.ExternalId == candidate.IgdbId
                    select g).SingleOrDefaultAsync(ct);
                if (game is not null) selected = Local(game);
            }
            if (!results.Any(r => selected.GameId is not null ? r.GameId == selected.GameId : r.IgdbId == selected.IgdbId))
                results.Add(selected);
        }
        return results;
    }

    private async Task<ManualMappingSearchResultDto> VerifySelection(string? gameId, string? igdbId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(gameId) && !string.IsNullOrWhiteSpace(igdbId))
            throw AppProblemException.Validation("Select one saved game or IGDB result.");
        var selected = ManualService.ValidateGameSelection(gameId, igdbId);
        if (selected.GameId.HasValue)
            return Local(ManualService.RequireExistingGame(await manualRepository.GetGameEntity(selected.GameId.Value, ct)));
        var resolved = await manual.ResolveAuthoritativeSelection(selected, ct);
        var game = resolved.IgdbResolution!;
        return new("igdb", null, game.ExternalId, game.Title, null, null, game.CapsuleUrl);
    }

    private async Task<SessionDraftBatch> RequireBatch(long userId, Guid id, CancellationToken ct) =>
        await repository.FindBatch(userId, id, ct) ?? throw AppProblemException.NotFound();

    private static ManualMappingSearchResultDto Local(Game game) =>
        new("local", GameId.Encode(game.Id), null, game.Title, null, null, game.CapsuleUrl);
    private static ManualMappingSearchResultDto PreviouslyPlayed(GameSearchRow game) =>
        new("local", GameId.Encode(game.Id), null, game.Title, game.Platform, null, game.CapsuleUrl, true);
    private static void RequirePending(SessionDraftRow row, int revision)
    {
        if (row.Status != "pending") throw AppProblemException.Conflict("This draft is no longer pending.");
        if (row.Revision != revision) throw AppProblemException.Conflict("This draft changed. Reload it before continuing.");
    }
    /// <summary>The time zone the batch was drafted in: its dates are days there.</summary>
    private static TimeZoneInfo? Zone(SessionDraftBatch batch) => LocalCalendar.FindTimeZone(batch.Timezone);

    public static DraftBatchDto ToDto(SessionDraftBatch batch)
    {
        var zone = Zone(batch);
        var now = DateTimeOffset.UtcNow;
        return new(batch.Id, batch.CreatedAt,
            batch.Rows.Where(r => r.Status != "deleted").OrderBy(r => r.Position).Select(row => ToDto(row, zone, now)).ToArray());
    }

    public static DraftRowDto ToDto(SessionDraftRow row, TimeZoneInfo? zone, DateTimeOffset now)
    {
        var selected = row.SelectionJson is null ? null : JsonSerializer.Deserialize<ManualMappingSearchResultDto>(row.SelectionJson);
        var warnings = row.Warnings.ToList();
        if (selected is null) warnings.Add("Select a game.");
        var timesProblem = DraftRules.Problem(row.Date, row.DurationMinutes, zone, now, out _, out _);
        if (timesProblem is not null) warnings.Add(timesProblem);
        if (row.PlatformKey == "unknown") warnings.Add("Platform is Unknown. You can choose a platform or accept Unknown.");
        return new(row.Id, row.Revision, row.Status, row.Title, row.Date, row.DurationMinutes, row.PlatformKey,
            selected, warnings.Distinct().ToArray(), row.Status == "pending" && selected is not null && timesProblem is null,
            row.AcceptedSessionId);
    }
}
