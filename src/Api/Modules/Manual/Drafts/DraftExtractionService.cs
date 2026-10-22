using System.Diagnostics.Metrics;
using System.Text.Json;
using Api.Infrastructure.Http;
using Api.Modules.Manual.Drafts.Extraction;
using Microsoft.Extensions.Options;
using Npgsql;
using Playdex.Persistence;

namespace Api.Modules.Manual.Drafts;

public sealed class DraftExtractionService(DraftRepository repository, DraftService drafts,
    ManualRepository manual, IPlaySessionExtractor extractor, IOptions<AiDraftOptions> options,
    NpgsqlDataSource dataSource, TimeProvider clock, GenerationHistoryRecorder history)
{
    private static readonly Meter Meter = new("Playdex.AiDrafts");
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("playdex.ai_drafts.extractions");

    public async Task<DraftBatchDto> Create(long userId, Guid requestId, string timezone,
        string? text, byte[]? image, string? contentType, CancellationToken ct)
    {
        if (requestId == Guid.Empty) throw AppProblemException.Validation("A request ID is required.");
        var existing = await repository.FindRequest(userId, requestId, ct);
        if (existing is not null) return DraftService.ToDto(existing);
        var opts = options.Value;
        if (!opts.IsAvailable) throw new AppProblemException(503, "AI extraction is not configured.");
        var day = ReferenceDate(timezone, clock.GetUtcNow());
        var platforms = await manual.ListPlatforms(ct);
        var input = new ExtractionInput(text, image, contentType, day, platforms.Where(p => p.Key != ManualService.PlaynitePlatformKey).Select(p => p.Key).ToArray());
        // Only cheap checks before the lock: decoding a screenshot is the expensive part, and parallel uploads
        // must not get to do it without taking the lock and spending quota.
        ExtractionInputValidator.ValidateRequest(input, opts);

        // A dedicated session advisory lock spans the external request without holding a transaction.
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var key = $"draft-extraction-user:{userId}";
        await using var acquire = new NpgsqlCommand("select pg_try_advisory_lock(hashtextextended(@key,0))", connection);
        acquire.Parameters.AddWithValue("key", key);
        if (await acquire.ExecuteScalarAsync(ct) is not true)
            throw AppProblemException.Conflict("An extraction is already running. Retry this request when it finishes.");
        Guid? generationId = null;
        try
        {
            existing = await repository.FindRequest(userId, requestId, ct);
            if (existing is not null) return DraftService.ToDto(existing);
            await CheckQuota(connection, userId, opts.RequestsPerHour, ct);
            ExtractionInputValidator.ValidateImage(input, opts);
            generationId = await history.Begin(userId, requestId, timezone, input);
            input = input with { ObserveResponse = response => history.RecordResponse(generationId, response) };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds((long)opts.TimeoutSeconds * 2));
            var rows = await extractor.Extract(input, timeout.Token);
            if (rows.Count == 0) throw AppProblemException.Validation("No individual play sessions could be found. Add dates and durations or try a clearer screenshot.");
            if (rows.Count > opts.MaxRows) throw new AppProblemException(502, "Too many sessions were extracted. Try a smaller input.");
            var batch = new SessionDraftBatch { Id=Guid.NewGuid(), UserId=userId, RequestId=requestId,
                CreatedAt=clock.GetUtcNow(), Timezone=timezone, ReferenceDate=day };
            foreach (var row in rows)
            {
                var candidates = await drafts.SearchCandidates(userId, row.Title, timeout.Token);
                var playedCandidates = candidates.Where(candidate => candidate.PreviouslyPlayed).ToArray();
                var selected = playedCandidates.Length == 1
                    ? playedCandidates[0]
                    : candidates.Count == 1 ? candidates[0] : null;
                var platform = platforms.Any(p => p.Key == row.PlatformKey && p.Key != ManualService.PlaynitePlatformKey) ? row.PlatformKey! : "unknown";
                var warnings = row.Warnings.ToList();
                if (platform == "unknown"
                    && selected?.PreviouslyPlayed == true
                    && platforms.Any(candidate => candidate.Key == selected.Platform && candidate.Key != ManualService.PlaynitePlatformKey))
                {
                    platform = selected.Platform!;
                    warnings.RemoveAll(warning => warning.StartsWith("Platform:", StringComparison.OrdinalIgnoreCase));
                }
                batch.Rows.Add(new SessionDraftRow { Id=Guid.NewGuid(), BatchId=batch.Id, Position=batch.Rows.Count,
                    Title=row.Title, Date=row.Date, DurationMinutes=row.DurationMinutes, PlatformKey=platform,
                    Warnings=warnings.ToArray(), GameId=selected?.GameId, IgdbId=selected?.IgdbId,
                    SelectionJson=selected is null ? null : JsonSerializer.Serialize(selected) });
            }
            var saved = await repository.CreateBatch(batch, ct);
            var result = DraftService.ToDto(saved);
            await history.Complete(generationId, "success", result: result);
            Requests.Add(1, new KeyValuePair<string,object?>("outcome","success"));
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await history.Complete(generationId, "timeout", "AI extraction timed out.");
            Requests.Add(1,new KeyValuePair<string,object?>("outcome","timeout"));
            throw new AppProblemException(504,"AI extraction timed out. Retry with a smaller input.");
        }
        catch (Exception exception)
        {
            await history.Complete(generationId, ct.IsCancellationRequested ? "cancelled"
                : exception is AppProblemException {StatusCode:504} ? "timeout" : "failure",
                exception is AppProblemException problem ? problem.Message : "AI generation did not complete.");
            Requests.Add(1,new KeyValuePair<string,object?>("outcome","failure"));
            throw;
        }
        finally
        {
            await using var release = new NpgsqlCommand("select pg_advisory_unlock(hashtextextended(@key,0))",connection);
            release.Parameters.AddWithValue("key",key);
            await release.ExecuteScalarAsync(CancellationToken.None);
        }
    }

    public static DateOnly ReferenceDate(string timezone, DateTimeOffset now)
    {
        try { return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(timezone)).DateTime); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { throw AppProblemException.Validation("Select a valid timezone."); }
    }

    private static async Task CheckQuota(NpgsqlConnection connection, long userId, int limit, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            insert into draft_extraction_attempts(user_id,window_started_at,attempt_count)
            values (@user,now(),1)
            on conflict(user_id) do update set
                window_started_at=case when draft_extraction_attempts.window_started_at <= now()-interval '1 hour' then now() else draft_extraction_attempts.window_started_at end,
                attempt_count=case when draft_extraction_attempts.window_started_at <= now()-interval '1 hour' then 1 else draft_extraction_attempts.attempt_count+1 end
            where draft_extraction_attempts.window_started_at <= now()-interval '1 hour' or draft_extraction_attempts.attempt_count < @limit
            returning attempt_count
            """,connection);
        command.Parameters.AddWithValue("user",userId);
        command.Parameters.AddWithValue("limit",limit);
        if (await command.ExecuteScalarAsync(ct) is null)
            throw new AppProblemException(429,"Hourly AI extraction limit reached. Try again later.");
    }
}
