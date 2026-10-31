using System.Collections.Concurrent;
using System.Text.Json;
using Api.Infrastructure.Common;
using Api.Infrastructure.Http;
using Api.Modules.Manual.Drafts.Extraction;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Api.Modules.Manual.Drafts;

public sealed class DraftService(DraftRepository repository, PlaydexDbContext db,
    ManualService manual, ManualRepository manualRepository)
{
    /// <summary>IGDB searches one extraction runs at once (IGDB allows four requests per second).</summary>
    public const int MaxParallelIgdbSearches = 3;

    /// <summary>Rows one batch may hold; review accepts at most this many at once.</summary>
    public const int MaxRows = 50;

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
        if (title.Length > 500)
        {
            throw AppProblemException.Validation("Game title must be at most 500 characters.");
        }

        if (request.Date?.Length > 10)
        {
            throw AppProblemException.Validation("Use a calendar date in yyyy-MM-dd format.");
        }

        var platform = request.PlatformKey?.Trim().ToLowerInvariant() ?? "unknown";
        // The catalog the review offers excludes Playnite, as extraction does; an edit cannot bring it back.
        if (platform == ManualService.PlaynitePlatformKey || await manualRepository.GetPlatformIdByKey(platform, ct) is null)
        {
            throw AppProblemException.Validation("Select a platform from the catalog.");
        }

        ManualMappingSearchResultDto? selected = null;
        // A changed query invalidates the prior selection, even when a client sends its stale ID.
        if (title == existing.Title && (!string.IsNullOrWhiteSpace(request.GameId) || !string.IsNullOrWhiteSpace(request.IgdbId)))
        {
            selected = await VerifySelection(request.GameId, request.IgdbId, ct);
        }

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
        // Loaded once: each row is re-read and its revision checked under a row lock before it is accepted, so a
        // snapshot taken here cannot accept a row that changed since.
        var batch = await RequireBatch(userId, batchId, ct);
        if (request.Rows is null || request.Rows.Count is < 1 or > MaxRows || request.Rows.Select(r => r.Id).Distinct().Count() != request.Rows.Count)
        {
            throw AppProblemException.Validation("Select between 1 and 50 different draft rows.");
        }

        var results = new List<DraftAcceptResult>();
        foreach (var requested in request.Rows)
        {
            try
            {
                var snapshot = batch.Rows.SingleOrDefault(r => r.Id == requested.Id) ?? throw AppProblemException.NotFound();
                if (snapshot.Status == "accepted")
                {
                    results.Add(new(snapshot.Id, snapshot.AcceptedSessionId, null));
                    continue;
                }
                RequirePending(snapshot, requested.Revision);
                var zone = Zone(batch);
                if (!DraftRules.TryGetTimes(
                        snapshot.Date, snapshot.DurationMinutes, zone, DateTimeOffset.UtcNow, out var start, out var end))
                {
                    throw AppProblemException.Validation("Correct the date and duration before accepting.");
                }

                var entry = ManualEntryDay.Of(LocalCalendar.DateOf(start, zone ?? TimeZoneInfo.Utc), zone, end - start);
                var selection = ManualService.ValidateGameSelection(snapshot.GameId, snapshot.IgdbId);
                // IGDB fetches happen before a database transaction, then revision is checked under lock.
                var authoritative = await manual.ResolveAuthoritativeSelection(selection, ct);
                var sessionId = await repository.WithLockedRow(userId, batchId, requested.Id, async row =>
                {
                    if (row.Status == "accepted")
                    {
                        return row.AcceptedSessionId!.Value;
                    }

                    RequirePending(row, requested.Revision);
                    if (authoritative.GameId.HasValue)
                    {
                        ManualService.RequireExistingGame(await manualRepository.GetGameEntity(authoritative.GameId.Value, ct));
                    }

                    var platformId = await manualRepository.GetPlatformIdByKey(row.PlatformKey, ct)
                        ?? throw AppProblemException.Validation("Platform not found.");
                    var gameId = await manual.ResolveGame(authoritative, ct);
                    // Rows of one date follow each other instead of all starting at midnight (ManualService.PlaceEntry).
                    var (placedStart, placedEnd) = await manual.PlaceEntry(userId, platformId, entry, ct);
                    var id = await manualRepository.CreatePlaySession(userId, gameId, platformId, placedStart, placedEnd, ct);
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

    /// <summary>
    /// A batch from sessions a client already parsed, as an assistant does over MCP: the same game matching and
    /// review as AI extraction, without the server's model or its quota. Each call is a new batch.
    /// </summary>
    public async Task<DraftBatchDto> CreateFromRows(long userId, string timezone, IReadOnlyList<ExtractedSession>? rows,
        TimeSpan searchBudget, DateTimeOffset now, CancellationToken ct)
    {
        if (rows is null || rows.Count is < 1 or > MaxRows)
        {
            throw AppProblemException.Validation($"Send between 1 and {MaxRows} sessions.");
        }

        var cleaned = new List<ExtractedSession>(rows.Count);
        foreach (var row in rows)
        {
            var title = row.Title?.Trim() ?? "";
            if (title.Length is 0 or > 500)
            {
                throw AppProblemException.Validation("Each session needs a game title of at most 500 characters.");
            }

            // A malformed date or duration is kept for the review to correct, as extraction keeps it.
            var date = row.Date?.Trim() is { Length: > 0 and <= 10 } d ? d : null;
            var platform = row.PlatformKey?.Trim().ToLowerInvariant();
            cleaned.Add(new(title, date, row.DurationMinutes, platform, []));
        }

        var day = DraftExtractionService.ReferenceDate(timezone, now);
        return await SaveBatch(userId, Guid.NewGuid(), timezone, day, now, cleaned, searchBudget, ct);
    }

    /// <summary>
    /// Saves sessions as a pending batch: each row gets the game it most likely means (the one game the user played
    /// under a similar title, else the only candidate) and, when its platform is unknown, the platform the user last
    /// played that game on. A row whose IGDB search runs out of <paramref name="searchBudget"/> is saved without
    /// IGDB candidates. A repeated <paramref name="requestId"/> returns the batch saved for it.
    /// </summary>
    internal async Task<DraftBatchDto> SaveBatch(long userId, Guid requestId, string timezone, DateOnly referenceDate,
        DateTimeOffset createdAt, IReadOnlyList<ExtractedSession> rows, TimeSpan searchBudget, CancellationToken ct)
    {
        var platforms = (await manualRepository.ListPlatforms(ct))
            .Where(p => p.Key != ManualService.PlaynitePlatformKey).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var searches = await SearchCandidates(userId, rows.Select(row => row.Title), searchBudget, ct);
        var batch = new SessionDraftBatch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RequestId = requestId,
            CreatedAt = createdAt,
            Timezone = timezone,
            ReferenceDate = referenceDate
        };
        foreach (var row in rows)
        {
            var candidates = searches.GetValueOrDefault(row.Title.Trim()) ?? [];
            var playedCandidates = candidates.Where(candidate => candidate.PreviouslyPlayed).ToArray();
            var selected = playedCandidates.Length == 1
                ? playedCandidates[0]
                : candidates.Count == 1 ? candidates[0] : null;
            var platform = row.PlatformKey is { } key && platforms.Contains(key) ? key : "unknown";
            var warnings = row.Warnings.ToList();
            if (platform == "unknown" && selected?.PreviouslyPlayed == true && selected.Platform is { } played
                && platforms.Contains(played))
            {
                platform = played;
                warnings.RemoveAll(warning => warning.StartsWith("Platform:", StringComparison.OrdinalIgnoreCase));
            }
            batch.Rows.Add(new SessionDraftRow
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                Position = batch.Rows.Count,
                Title = row.Title,
                Date = row.Date,
                DurationMinutes = row.DurationMinutes,
                PlatformKey = platform,
                Warnings = warnings.ToArray(),
                GameId = selected?.GameId,
                IgdbId = selected?.IgdbId,
                SelectionJson = selected is null ? null : JsonSerializer.Serialize(selected)
            });
        }

        return ToDto(await repository.CreateBatch(batch, ct));
    }

    /// <summary>
    /// Games a draft title may mean. Extracted titles can run to 500 characters; past the search limit they get no
    /// candidates, and the reviewer searches with a shorter title instead.
    /// </summary>
    public async Task<IReadOnlyList<ManualMappingSearchResultDto>> SearchCandidates(
        long userId,
        string title,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > SearchText.MaxLength)
        {
            return [];
        }

        var candidates = await SearchCandidates(userId, [title], igdbBudget: null, ct);
        return candidates[title.Trim()];
    }

    /// <summary>
    /// Candidates for several titles, keyed by trimmed title: games the user played, then saved games, then IGDB
    /// results (a result already saved as a game is shown as that game). IGDB is searched in parallel, at most
    /// <see cref="MaxParallelIgdbSearches"/> at a time, within <paramref name="igdbBudget"/>; a title whose search
    /// fails or runs out of budget gets no IGDB results. The database is queried one title at a time, and the saved
    /// games behind IGDB results are loaded in one query. A title longer than <see cref="SearchText.MaxLength"/> is
    /// not searched and has no entry.
    /// </summary>
    internal async Task<IReadOnlyDictionary<string, IReadOnlyList<ManualMappingSearchResultDto>>> SearchCandidates(
        long userId,
        IEnumerable<string> titles,
        TimeSpan? igdbBudget,
        CancellationToken ct)
    {
        var queries = titles.Select(title => title.Trim())
            .Where(title => title.Length > 0 && title.Length <= SearchText.MaxLength)
            .Distinct(StringComparer.Ordinal).ToArray();
        var igdb = await SearchIgdb(queries, igdbBudget, ct);

        var igdbIds = igdb.Values.SelectMany(results => results).Select(result => result.Id).Distinct().ToArray();
        var savedByIgdbId = igdbIds.Length == 0
            ? []
            : await (from identity in db.GameExternalIdentities.AsNoTracking()
                     join game in db.Games.AsNoTracking() on identity.GameId equals game.Id
                     where identity.Type == GameExternalIdentityType.Igdb && igdbIds.Contains(identity.ExternalId)
                     select new { identity.ExternalId, Game = game })
                .ToDictionaryAsync(row => row.ExternalId, row => row.Game, StringComparer.Ordinal, ct);

        var candidates = new Dictionary<string, IReadOnlyList<ManualMappingSearchResultDto>>(StringComparer.Ordinal);
        foreach (var query in queries)
        {
            var results = (await manualRepository.SearchRecentlyPlayedGames(userId, query, 5, ct))
                .Select(PreviouslyPlayed).ToList();
            var others = (await manual.SearchLocal(query, ct)).Concat(igdb[query].Select(result =>
                savedByIgdbId.TryGetValue(result.Id, out var saved) ? Local(saved) : ManualService.IgdbResult(result)));
            foreach (var candidate in others)
            {
                if (!results.Any(r => candidate.GameId is not null ? r.GameId == candidate.GameId : r.IgdbId == candidate.IgdbId))
                {
                    results.Add(candidate);
                }
            }

            candidates[query] = results;
        }

        return candidates;
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<Polling.IgdbSearchResult>>> SearchIgdb(
        IReadOnlyList<string> queries,
        TimeSpan? budget,
        CancellationToken ct)
    {
        using var budgetTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (budget is { } limit)
        {
            budgetTimeout.CancelAfter(limit);
        }

        var results = new ConcurrentDictionary<string, IReadOnlyList<Polling.IgdbSearchResult>>(StringComparer.Ordinal);
        await Parallel.ForEachAsync(
            queries,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelIgdbSearches, CancellationToken = ct },
            async (query, _) =>
            {
                try
                {
                    results[query] = (await manual.SearchIgdb(query, budgetTimeout.Token)).Results;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    results[query] = [];
                }
            });
        return results;
    }

    private async Task<ManualMappingSearchResultDto> VerifySelection(string? gameId, string? igdbId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(gameId) && !string.IsNullOrWhiteSpace(igdbId))
        {
            throw AppProblemException.Validation("Select one saved game or IGDB result.");
        }

        var selected = ManualService.ValidateGameSelection(gameId, igdbId);
        if (selected.GameId.HasValue)
        {
            return Local(ManualService.RequireExistingGame(await manualRepository.GetGameEntity(selected.GameId.Value, ct)));
        }

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
        if (row.Status != "pending")
        {
            throw AppProblemException.Conflict("This draft is no longer pending.");
        }

        if (row.Revision != revision)
        {
            throw AppProblemException.Conflict("This draft changed. Reload it before continuing.");
        }
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
        if (selected is null)
        {
            warnings.Add("Select a game.");
        }

        var timesProblem = DraftRules.Problem(row.Date, row.DurationMinutes, zone, now, out _, out _);
        if (timesProblem is not null)
        {
            warnings.Add(timesProblem);
        }

        if (row.PlatformKey == "unknown")
        {
            warnings.Add("Platform is Unknown. You can choose a platform or accept Unknown.");
        }

        return new(row.Id, row.Revision, row.Status, row.Title, row.Date, row.DurationMinutes, row.PlatformKey,
            selected, warnings.Distinct().ToArray(), row.Status == "pending" && selected is not null && timesProblem is null,
            row.AcceptedSessionId);
    }
}
