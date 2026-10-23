using System.Globalization;
using Api.Infrastructure.Common;
using Api.Modules.Games;

namespace Api.Modules.Recap;

internal sealed record SessionData(
    long SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    long GameId,
    string Game,
    string? CapsuleUrl,
    string PlatformKey,
    string PlatformName);

internal sealed record GameAccum(
    string Title,
    string? CapsuleUrl,
    double TotalSeconds,
    int SessionCount);

internal sealed record GroupAccum(
    long RepresentativeId,
    string RepresentativeTitle,
    string? RepresentativeCapsuleUrl,
    double RepresentativeSeconds,
    double TotalSeconds,
    int SessionCount);

internal sealed record PlatformAccum(
    string PlatformKey,
    string PlatformName,
    int SessionCount,
    double TotalSeconds);

internal sealed record GroupSessionTimes(
    List<long> Timestamps,
    HashSet<string> Months);

internal sealed record LongestSessionInfo(
    long Root,
    double Seconds);

internal sealed record BingedInfo(
    long Root,
    int SessionCount);

internal static class RecapRange
{
    /// <summary>
    /// Where the recap's day counts end: the requested end, but never after today in <paramref name="zone"/>, so the
    /// current year does not count its future days as days not played.
    /// </summary>
    public static DateTimeOffset ResolveEndExclusive(
        DateTimeOffset now,
        DateTimeOffset? requestedEndExclusive,
        TimeZoneInfo zone)
    {
        var tomorrow = LocalCalendar.StartOfNextDay(now, zone);
        return requestedEndExclusive is { } end && end < tomorrow ? end : tomorrow;
    }

    /// <summary>Calendar days of <paramref name="zone"/> from the day <paramref name="start"/> falls on up to <paramref name="endExclusive"/>.</summary>
    public static int CountDays(DateTimeOffset start, DateTimeOffset endExclusive, TimeZoneInfo zone) =>
        Math.Max(0, LocalCalendar.DateOf(endExclusive, zone).DayNumber - LocalCalendar.DateOf(start, zone).DayNumber);

    /// <summary>Calendar months of <paramref name="zone"/> touched by [<paramref name="start"/>, <paramref name="endExclusive"/>).</summary>
    public static int CountMonths(DateTimeOffset start, DateTimeOffset endExclusive, TimeZoneInfo zone)
    {
        if (endExclusive <= start)
        {
            return 0;
        }

        var first = LocalCalendar.DateOf(start, zone);
        var last = LocalCalendar.DateOf(endExclusive, zone).AddDays(-1);
        return Math.Max(0, (last.Year - first.Year) * 12 + (last.Month - first.Month) + 1);
    }
}

public sealed class RecapService(RecapRepository repository)
{
    private static readonly long DayMs = (long)TimeSpan.FromDays(1).TotalMilliseconds;

    public async Task<RecapYearsResponseDto> GetYears(long userId, TimeZoneInfo zone, CancellationToken ct)
    {
        var years = await repository.ListRecapYears(userId, zone, ct);
        return new RecapYearsResponseDto(years);
    }

    /// <summary>
    /// The recap of sessions started in [<paramref name="startedAt"/>, <paramref name="endedAt"/>); days, months,
    /// weekdays and hours are those of <paramref name="zone"/>.
    /// </summary>
    public async Task<RecapResponseDto> GetRecap(
        long userId,
        string? from,
        string? to,
        DateTimeOffset? startedAt,
        DateTimeOffset? endedAt,
        TimeZoneInfo zone,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // ── Load data ──────────────────────────────────────────
        var rawSessions = await repository.ListRecapSessions(userId, startedAt, endedAt, ct);
        var sessions = await MapSessionsForUser(userId, rawSessions, ct);
        var edges = await repository.ListConnectionEdges(userId, ct);

        // Build Union-Find index for game connections
        var connectionIndex = BuildConnectionIndex(edges);

        // ── Per-session accumulators ───────────────────────────
        var durations = new List<double>(sessions.Count);
        double totalSeconds = 0;

        var dayCounts = new Dictionary<string, int>();
        var monthCounts = new Dictionary<string, int>();
        var daySet = new HashSet<string>();

        var weekdayCounts = new int[7];
        var hourCounts = new int[24];
        double weekendSeconds = 0;
        double weekdaySeconds = 0;

        var platformTotals = new Dictionary<string, PlatformAccum>(StringComparer.Ordinal);
        var gameTotals = new Dictionary<long, GameAccum>();

        var groupSessionTimes = new Dictionary<long, GroupSessionTimes>();
        long? longestSessionGroup = null;
        double longestSessionSeconds = 0;

        var i = 0;
        foreach (var session in sessions)
        {
            if (i++ % 1000 == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            var startedMs = session.StartedAt.ToUnixTimeMilliseconds();
            var endedMs = session.EndedAt?.ToUnixTimeMilliseconds() ?? now.ToUnixTimeMilliseconds();
            var durationSeconds = Math.Max(0, Math.Floor((endedMs - startedMs) / 1000.0));
            durations.Add(durationSeconds);
            totalSeconds += durationSeconds;

            var localStart = TimeZoneInfo.ConvertTime(session.StartedAt, zone);
            var dayKey = ToDateKey(localStart);
            daySet.Add(dayKey);
            dayCounts[dayKey] = dayCounts.GetValueOrDefault(dayKey) + 1;

            var monthKey = ToMonthKey(localStart);
            monthCounts[monthKey] = monthCounts.GetValueOrDefault(monthKey) + 1;

            var dayOfWeek = (int)localStart.DayOfWeek;
            var weekdayIndex = dayOfWeek == 0 ? 6 : dayOfWeek - 1; // Monday=0 .. Sunday=6
            weekdayCounts[weekdayIndex]++;
            hourCounts[localStart.Hour]++;

            if (localStart.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                weekendSeconds += durationSeconds;
            }
            else
            {
                weekdaySeconds += durationSeconds;
            }

            // Platform totals
            var platformKey = session.PlatformKey;
            if (platformTotals.TryGetValue(platformKey, out var existingPt))
            {
                platformTotals[platformKey] = existingPt with
                {
                    SessionCount = existingPt.SessionCount + 1,
                    TotalSeconds = existingPt.TotalSeconds + durationSeconds
                };
            }
            else
            {
                platformTotals[platformKey] = new PlatformAccum(
                    platformKey, session.PlatformName, 1, durationSeconds);
            }

            // Game totals
            if (gameTotals.TryGetValue(session.GameId, out var existingGt))
            {
                gameTotals[session.GameId] = existingGt with
                {
                    TotalSeconds = existingGt.TotalSeconds + durationSeconds,
                    SessionCount = existingGt.SessionCount + 1,
                    CapsuleUrl = existingGt.CapsuleUrl ?? session.CapsuleUrl
                };
            }
            else
            {
                gameTotals[session.GameId] = new GameAccum(
                    session.Game, session.CapsuleUrl, durationSeconds, 1);
            }

            // Group session times (for most binged)
            var root = connectionIndex.Find(session.GameId);
            if (groupSessionTimes.TryGetValue(root, out var existingGst))
            {
                existingGst.Timestamps.Add(startedMs);
                existingGst.Months.Add(monthKey);
            }
            else
            {
                groupSessionTimes[root] = new GroupSessionTimes([startedMs], [monthKey]);
            }

            // Track longest session per group
            if (durationSeconds > longestSessionSeconds)
            {
                longestSessionSeconds = durationSeconds;
                longestSessionGroup = root;
            }
        }

        var totalSessions = sessions.Count;

        // ── Compute aggregate stats ────────────────────────────

        var averageSessionSeconds = totalSessions > 0 ? totalSeconds / totalSessions : 0;
        var medianSessionSeconds = ComputeMedianSeconds(durations);
        var longestSessionSecondsOut = durations.Count > 0 ? durations.Max() : 0;
        var shortestSessionSeconds = durations.Count > 0 ? durations.Min() : 0;

        var mostActiveDay = ComputeMostActive(dayCounts);
        var mostActiveMonth = ComputeMostActive(monthCounts);

        // Streak calculation
        var sortedDayKeys = daySet.OrderBy(k => k).ToList();
        var longestStreakDays = 0;
        var currentStreak = 0;
        DateTime? previousDay = null;

        foreach (var key in sortedDayKeys)
        {
            var dayDate = DateTime.Parse(key, CultureInfo.InvariantCulture);
            if (previousDay is null || (dayDate - previousDay.Value).Days != 1)
            {
                currentStreak = 1;
            }
            else
            {
                currentStreak++;
            }

            longestStreakDays = Math.Max(longestStreakDays, currentStreak);
            previousDay = dayDate;
        }

        // Date range for averages: whole local days, ending today at the latest.
        var firstDay = startedAt.HasValue
            ? LocalCalendar.DateOf(startedAt.Value, zone)
            : LocalCalendar.DateOf(sessions.Count > 0 ? sessions[0].StartedAt : now, zone);
        var rangeStart = LocalCalendar.StartOf(firstDay, zone);
        var rangeEndExclusive = RecapRange.ResolveEndExclusive(now, endedAt, zone);

        var daysInRange = RecapRange.CountDays(rangeStart, rangeEndExclusive, zone);
        var daysPlayed = daySet.Count;
        var daysNotPlayed = Math.Max(0, daysInRange - daysPlayed);

        var averageSessionsPerDay = daysInRange > 0 ? (double)totalSessions / daysInRange : 0;
        var averageSessionsPerWeek = daysInRange > 0 ? (double)totalSessions / (daysInRange / 7.0) : 0;
        var monthsInRange = RecapRange.CountMonths(rangeStart, rangeEndExclusive, zone);
        var averageSessionsPerMonth = monthsInRange > 0 ? (double)totalSessions / monthsInRange : 0;

        // ── Weekday / hour distributions ───────────────────────

        var sessionsByWeekday = weekdayCounts
            .Select((count, index) => new WeekdayStatDto(index, count))
            .ToList();

        var sessionsByHour = hourCounts
            .Select((count, hour) => new HourStatDto(hour, count))
            .ToList();

        HourStatDto? peakHour = null;
        if (totalSessions > 0)
        {
            var best = sessionsByHour.MaxBy(h => h.SessionCount);
            peakHour = best;
        }

        // ── Group totals (merge connected games) ───────────────

        var groupTotals = new Dictionary<long, GroupAccum>();

        foreach (var (gameId, game) in gameTotals)
        {
            var rootId = connectionIndex.Find(gameId);
            if (groupTotals.TryGetValue(rootId, out var existingGroup))
            {
                var newTotalSeconds = existingGroup.TotalSeconds + game.TotalSeconds;
                var newSessionCount = existingGroup.SessionCount + game.SessionCount;

                GroupAccum updated;
                if (game.TotalSeconds > existingGroup.RepresentativeSeconds)
                {
                    updated = existingGroup with
                    {
                        RepresentativeId = gameId,
                        RepresentativeTitle = game.Title,
                        RepresentativeCapsuleUrl = game.CapsuleUrl,
                        RepresentativeSeconds = game.TotalSeconds,
                        TotalSeconds = newTotalSeconds,
                        SessionCount = newSessionCount
                    };
                }
                else
                {
                    updated = existingGroup with
                    {
                        TotalSeconds = newTotalSeconds,
                        SessionCount = newSessionCount
                    };
                }
                groupTotals[rootId] = updated;
            }
            else
            {
                groupTotals[rootId] = new GroupAccum(
                    gameId, game.Title, game.CapsuleUrl, game.TotalSeconds,
                    game.TotalSeconds, game.SessionCount);
            }
        }

        var groupEntries = groupTotals.Values
            .Select(g => new RecapGameStatDto(
                GameId.Encode(g.RepresentativeId),
                g.RepresentativeTitle,
                g.RepresentativeCapsuleUrl,
                g.TotalSeconds,
                g.SessionCount,
                g.SessionCount > 0 ? g.TotalSeconds / g.SessionCount : 0,
                g.SessionCount == 1))
            .ToList();

        // Build map of root -> representative info (for longest session / most binged lookups)
        var groupRepresentatives = groupTotals.ToDictionary(
            kv => kv.Key,
            kv => (kv.Value.RepresentativeId, kv.Value.RepresentativeTitle));

        // ── Top games ──────────────────────────────────────────

        var topGamesByTime = groupEntries
            .OrderByDescending(g => g.TotalSeconds)
            .ToList();

        var topGameByTime = topGamesByTime.Count > 0 ? topGamesByTime[0] : null;

        var topGameBySessions = groupEntries
            .OrderByDescending(g => g.SessionCount)
            .ThenByDescending(g => g.TotalSeconds)
            .FirstOrDefault();

        var topGameByAverageSession = groupEntries
            .OrderByDescending(g => g.AverageSessionSeconds)
            .ThenByDescending(g => g.TotalSeconds)
            .FirstOrDefault();

        var uniqueGames = groupEntries.Count;

        // ── Share of play time on the top three games (0-100) ──

        var topThreeGamesSharePercent = totalSeconds > 0
            ? topGamesByTime.Take(3).Sum(g => g.TotalSeconds) / totalSeconds * 100
            : 0;

        // ── Abandoned games ────────────────────────────────────

        var abandonedGames = groupEntries.Count(g => g.IsAbandoned);

        // ── Return rate games ──────────────────────────────────

        var returnRateGames = groupSessionTimes.Values.Count(gst => gst.Months.Count > 1);

        // ── Most binged game ───────────────────────────────────

        BingedGameDto? mostBingedGame = null;
        {
            BingedInfo? best = null;
            foreach (var (rootId, gst) in groupSessionTimes)
            {
                if (gst.Timestamps.Count == 0)
                {
                    continue;
                }

                var sorted = gst.Timestamps.OrderBy(t => t).ToList();
                var maxCount = 1;
                var left = 0;
                for (var right = 0; right < sorted.Count; right++)
                {
                    while (sorted[right] - sorted[left] > DayMs)
                    {
                        left++;
                    }

                    maxCount = Math.Max(maxCount, right - left + 1);
                }

                if (best is null || maxCount > best.SessionCount)
                {
                    best = new BingedInfo(rootId, maxCount);
                }
            }

            if (best is not null && groupRepresentatives.TryGetValue(best.Root, out var rep))
            {
                mostBingedGame = new BingedGameDto(
                    GameId.Encode(rep.RepresentativeId),
                    rep.RepresentativeTitle,
                    best.SessionCount);
            }
        }

        // ── Game with longest session ──────────────────────────

        LongestSessionGameDto? gameWithLongestSession = null;
        if (longestSessionGroup.HasValue &&
            groupRepresentatives.TryGetValue(longestSessionGroup.Value, out var longestRep))
        {
            gameWithLongestSession = new LongestSessionGameDto(
                GameId.Encode(longestRep.RepresentativeId),
                longestRep.RepresentativeTitle,
                longestSessionSeconds);
        }

        // ── New games tried ────────────────────────────────────

        var firstSessionRows = await repository.ListFirstSessionsByGame(userId, ct);
        var firstSessions = await MapFirstSessionsForUser(userId, firstSessionRows, ct);

        var firstByGroup = new Dictionary<long, DateTimeOffset>();
        foreach (var fs in firstSessions)
        {
            var rootId = connectionIndex.Find(fs.GameId);
            if (firstByGroup.TryGetValue(rootId, out var existing))
            {
                if (fs.StartedAt < existing)
                {
                    firstByGroup[rootId] = fs.StartedAt;
                }
            }
            else
            {
                firstByGroup[rootId] = fs.StartedAt;
            }
        }

        var newGamesTried = firstByGroup.Values.Count(d => d >= rangeStart && d < rangeEndExclusive);

        // ── Platform totals ────────────────────────────────────

        var platformTotalsList = platformTotals.Values
            .OrderByDescending(p => p.TotalSeconds)
            .Select(p => new RecapPlatformStatDto(
                p.PlatformKey, p.PlatformName, p.SessionCount, p.TotalSeconds))
            .ToList();

        PlatformSummaryDto? mostUsedPlatform = null;
        if (platformTotalsList.Count > 0)
        {
            var top = platformTotalsList[0];
            mostUsedPlatform = new PlatformSummaryDto(
                top.PlatformKey, top.PlatformName, top.TotalSeconds, top.SessionCount);
        }

        // ── Build response ─────────────────────────────────────

        return new RecapResponseDto(
            Range: new RecapRangeDto(from, to),
            TotalSessions: totalSessions,
            TotalSeconds: totalSeconds,
            AverageSessionSeconds: averageSessionSeconds,
            MedianSessionSeconds: medianSessionSeconds,
            LongestSessionSeconds: longestSessionSecondsOut,
            ShortestSessionSeconds: shortestSessionSeconds,
            AverageSessionsPerDay: averageSessionsPerDay,
            AverageSessionsPerWeek: averageSessionsPerWeek,
            AverageSessionsPerMonth: averageSessionsPerMonth,
            MostActiveDay: mostActiveDay is not null
                ? new MostActiveDayDto(mostActiveDay.Value.Key, mostActiveDay.Value.Value)
                : null,
            MostActiveMonth: mostActiveMonth is not null
                ? new MostActiveMonthDto(mostActiveMonth.Value.Key, mostActiveMonth.Value.Value)
                : null,
            LongestStreakDays: longestStreakDays,
            DaysPlayed: daysPlayed,
            DaysNotPlayed: daysNotPlayed,
            SessionsByWeekday: sessionsByWeekday,
            SessionsByHour: sessionsByHour,
            PeakHour: peakHour,
            WeekendSeconds: weekendSeconds,
            WeekdaySeconds: weekdaySeconds,
            UniqueGames: uniqueGames,
            TopGameBySessions: topGameBySessions,
            TopGameByTime: topGameByTime,
            TopGamesByTime: topGamesByTime.Take(5).ToList(),
            TopGameByAverageSession: topGameByAverageSession,
            GameWithLongestSession: gameWithLongestSession,
            NewGamesTried: newGamesTried,
            AbandonedGames: abandonedGames,
            ReturnRateGames: returnRateGames,
            TopThreeGamesSharePercent: topThreeGamesSharePercent,
            MostBingedGame: mostBingedGame,
            PlatformTotals: platformTotalsList,
            MostUsedPlatform: mostUsedPlatform,
            GamesTable: topGamesByTime);
    }

    // ── Manual mapping logic ───────────────────────────────────

    private static Task<List<SessionData>> MapSessionsForUser(
        long userId, List<RecapSessionRow> rows, CancellationToken ct)
    {
        return Task.FromResult(rows.Select(ToSessionData).ToList());
    }

    private static async Task<List<SessionData>> MapFirstSessionsForUser(
        long userId, List<RecapFirstSessionRow> rows, CancellationToken ct)
    {
        // Map first sessions similarly — used for "new games tried" computation.
        // Convert first-session rows to the RecapSessionRow shape so we can reuse
        // the same mapping logic (manual title resolution, platform aliasing).
        var sessionRows = rows.Select(r => new RecapSessionRow
        {
            Id = 0,
            StartedAt = r.FirstStartedAt,
            EndedAt = r.FirstStartedAt,
            GameId = r.GameId,
            Title = r.Title,
            CapsuleUrl = r.CapsuleUrl,
            PlatformKey = r.PlatformKey,
            PlatformName = r.PlatformName
        }).ToList();

        var mapped = await MapSessionsForUser(userId, sessionRows, ct);
        return mapped.Select(s => s with { SessionId = 0 }).ToList();
    }

    // ── Helpers ────────────────────────────────────────────────

    private static SessionData ToSessionData(RecapSessionRow row) =>
        new(row.Id, row.StartedAt, row.EndedAt,
            row.GameId, row.Title, row.CapsuleUrl,
            row.PlatformKey, row.PlatformName);

    private static string ToDateKey(DateTimeOffset localDate) =>
        localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string ToMonthKey(DateTimeOffset localDate) =>
        localDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static double ComputeMedianSeconds(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2
            : sorted[mid];
    }

    private static KeyValuePair<string, int>? ComputeMostActive(Dictionary<string, int> counts)
    {
        KeyValuePair<string, int>? best = null;
        foreach (var kv in counts)
        {
            if (best is null || kv.Value > best.Value.Value)
            {
                best = kv;
            }
        }
        return best;
    }

    // ── Union-Find for game connections ────────────────────────

    private static ConnectionIndex BuildConnectionIndex(List<(long GameId, long ConnectedGameId)> edges)
    {
        var parent = new Dictionary<long, long>();

        long Find(long x)
        {
            if (!parent.TryGetValue(x, out var p))
            {
                parent[x] = x;
                return x;
            }
            if (p != x)
            {
                parent[x] = Find(p);
            }

            return parent[x];
        }

        void Union(long a, long b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra == rb)
            {
                return;
            }

            var min = Math.Min(ra, rb);
            var max = Math.Max(ra, rb);
            parent[max] = min;
        }

        foreach (var (gameId, connectedGameId) in edges)
        {
            Union(gameId, connectedGameId);
        }

        return new ConnectionIndex(Find);
    }

    private sealed record ConnectionIndex(Func<long, long> Find);
}
