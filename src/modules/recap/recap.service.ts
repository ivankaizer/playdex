import type { AccountSessionWithMetadata } from "../accounts/accounts.domain";
import {
  applyManualMappings,
  collectManualCanonicalTitles,
} from "../accounts/accounts.domain";
import { gamesService } from "../games";
import type { GameConnectionEdge } from "../games/gameConnections";
import { recapRepository, type RecapFirstSessionRow, type RecapSessionRow } from "./recap.repository";
import type { RecapStats } from "./recap.domain";

const DAY_MS = 24 * 60 * 60 * 1000;

const startOfUtcDay = (date: Date) =>
  new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate()));

const toUtcDateKey = (date: Date) => date.toISOString().slice(0, 10);

const toUtcMonthKey = (date: Date) => date.toISOString().slice(0, 7);

const countMonthsInRange = (start: Date, endExclusive: Date) => {
  if (endExclusive <= start) {
    return 0;
  }
  const end = new Date(endExclusive.getTime() - DAY_MS);
  const startYear = start.getUTCFullYear();
  const startMonth = start.getUTCMonth();
  const endYear = end.getUTCFullYear();
  const endMonth = end.getUTCMonth();
  return (endYear - startYear) * 12 + (endMonth - startMonth) + 1;
};

const createConnectionIndex = (edges: GameConnectionEdge[]) => {
  const parent = new Map<number, number>();
  const find = (id: number): number => {
    const existing = parent.get(id);
    if (existing === undefined) {
      parent.set(id, id);
      return id;
    }
    if (existing === id) {
      return id;
    }
    const root = find(existing);
    parent.set(id, root);
    return root;
  };
  const union = (left: number, right: number) => {
    const leftRoot = find(left);
    const rightRoot = find(right);
    if (leftRoot === rightRoot) {
      return;
    }
    const minRoot = Math.min(leftRoot, rightRoot);
    const maxRoot = Math.max(leftRoot, rightRoot);
    parent.set(maxRoot, minRoot);
  };

  edges.forEach((edge) => union(edge.game_id, edge.connected_game_id));

  return { find };
};

const toDomainSession = (
  row: RecapSessionRow
): AccountSessionWithMetadata => ({
  sessionId: Number(row.id),
  startedAt: row.started_at,
  endedAt: row.ended_at,
  gameId: Number(row.game_id),
  platform: row.platform,
  game: row.title,
  capsuleUrl: row.capsule_url,
  ignored: false,
  gamePlatform: row.game_platform,
  gameCanonicalKey: row.game_canonical_key,
});

const toDomainFirstSession = (
  row: RecapFirstSessionRow
): AccountSessionWithMetadata => ({
  sessionId: 0,
  startedAt: row.first_started_at,
  endedAt: row.first_started_at,
  gameId: Number(row.game_id),
  platform: row.platform,
  game: row.title,
  capsuleUrl: row.capsule_url,
  ignored: false,
  gamePlatform: row.game_platform,
  gameCanonicalKey: row.game_canonical_key,
});

const mapSessionsForUser = async (
  userId: number,
  rows: AccountSessionWithMetadata[]
) => {
  const manualTitles = collectManualCanonicalTitles(rows);
  const mappings =
    manualTitles.length > 0
      ? await recapRepository.loadManualMappings(userId, manualTitles)
      : [];
  return applyManualMappings(rows, mappings);
};

const computeMedianSeconds = (values: number[]) => {
  if (values.length === 0) {
    return 0;
  }
  const sorted = values.slice().sort((a, b) => a - b);
  const mid = Math.floor(sorted.length / 2);
  if (sorted.length % 2 === 0) {
    return (sorted[mid - 1] + sorted[mid]) / 2;
  }
  return sorted[mid];
};

export const recapService = {
  fetchRecapYears: async (userId: number) => {
    const rows = await recapRepository.listRecapYears(userId);
    return rows.map((row) => row.year).filter((year) => Number.isFinite(year));
  },

  fetchRecap: async (params: {
    userId: number;
    from: string | null;
    to: string | null;
    startedAt?: Date | null;
    endedAt?: Date | null;
  }): Promise<RecapStats> => {
    const { userId, from, to, startedAt, endedAt } = params;
    const now = new Date();
    const nowMs = now.getTime();

    const rawSessions = await recapRepository.listRecapSessions(
      userId,
      startedAt ?? null,
      endedAt ?? null
    );
    const domainSessions = rawSessions.map(toDomainSession);
    const sessions = await mapSessionsForUser(userId, domainSessions);
    const edges = await gamesService.listConnectionEdges(userId);
    const connectionIndex = createConnectionIndex(edges);

    const durations: number[] = [];
    let totalSeconds = 0;

    const dayCounts = new Map<string, number>();
    const monthCounts = new Map<string, number>();
    const daySet = new Set<string>();
    const weekdayCounts = Array.from({ length: 7 }, () => 0);
    const hourCounts = Array.from({ length: 24 }, () => 0);

    let weekendSeconds = 0;
    let weekdaySeconds = 0;

    const platformTotals = new Map<
      string,
      { platform: string; sessionCount: number; totalSeconds: number }
    >();

    const gameTotals = new Map<
      number,
      { title: string; totalSeconds: number; sessionCount: number }
    >();

    const groupSessionTimes = new Map<number, number[]>();
    const groupMonths = new Map<number, Set<string>>();
    let longestSession: { root: number; seconds: number } | null = null;

    sessions.forEach((session) => {
      const started = session.startedAt.getTime();
      const ended = session.endedAt ? session.endedAt.getTime() : nowMs;
      const durationSeconds = Math.max(0, Math.floor((ended - started) / 1000));
      durations.push(durationSeconds);
      totalSeconds += durationSeconds;

      const dayKey = toUtcDateKey(session.startedAt);
      daySet.add(dayKey);
      dayCounts.set(dayKey, (dayCounts.get(dayKey) ?? 0) + 1);

      const monthKey = toUtcMonthKey(session.startedAt);
      monthCounts.set(monthKey, (monthCounts.get(monthKey) ?? 0) + 1);

      const dayOfWeek = session.startedAt.getUTCDay();
      const weekdayIndex = (dayOfWeek + 6) % 7;
      weekdayCounts[weekdayIndex] += 1;
      hourCounts[session.startedAt.getUTCHours()] += 1;

      if (dayOfWeek === 0 || dayOfWeek === 6) {
        weekendSeconds += durationSeconds;
      } else {
        weekdaySeconds += durationSeconds;
      }

      const platformKey = session.mappedPlatform ?? session.platform;
      const platformEntry = platformTotals.get(platformKey);
      if (platformEntry) {
        platformEntry.sessionCount += 1;
        platformEntry.totalSeconds += durationSeconds;
      } else {
        platformTotals.set(platformKey, {
          platform: platformKey,
          sessionCount: 1,
          totalSeconds: durationSeconds,
        });
      }

      const gameEntry = gameTotals.get(session.gameId);
      if (gameEntry) {
        gameEntry.totalSeconds += durationSeconds;
        gameEntry.sessionCount += 1;
      } else {
        gameTotals.set(session.gameId, {
          title: session.game,
          totalSeconds: durationSeconds,
          sessionCount: 1,
        });
      }

      const root = connectionIndex.find(session.gameId);
      const times = groupSessionTimes.get(root);
      if (times) {
        times.push(started);
      } else {
        groupSessionTimes.set(root, [started]);
      }

      const monthSet = groupMonths.get(root);
      if (monthSet) {
        monthSet.add(monthKey);
      } else {
        groupMonths.set(root, new Set([monthKey]));
      }

      if (!longestSession || durationSeconds > longestSession.seconds) {
        longestSession = { root, seconds: durationSeconds };
      }
    });

    const totalSessions = sessions.length;
    const averageSessionSeconds =
      totalSessions > 0 ? totalSeconds / totalSessions : 0;
    const medianSessionSeconds = computeMedianSeconds(durations);
    const longestSessionSeconds = durations.length
      ? Math.max(...durations)
      : 0;
    const shortestSessionSeconds = durations.length
      ? Math.min(...durations)
      : 0;

    const mostActiveDay = (() => {
      let best: { date: string; sessionCount: number } | null = null;
      dayCounts.forEach((count, date) => {
        if (!best || count > best.sessionCount) {
          best = { date, sessionCount: count };
        }
      });
      return best;
    })();

    const mostActiveMonth = (() => {
      let best: { month: string; sessionCount: number } | null = null;
      monthCounts.forEach((count, month) => {
        if (!best || count > best.sessionCount) {
          best = { month, sessionCount: count };
        }
      });
      return best;
    })();

    const sortedDayKeys = Array.from(daySet.values()).sort();
    let longestStreakDays = 0;
    let currentStreak = 0;
    let previousDayMs: number | null = null;
    sortedDayKeys.forEach((key) => {
      const dayMs = Date.parse(key);
      if (previousDayMs === null || dayMs - previousDayMs !== DAY_MS) {
        currentStreak = 1;
      } else {
        currentStreak += 1;
      }
      longestStreakDays = Math.max(longestStreakDays, currentStreak);
      previousDayMs = dayMs;
    });

    const rangeStart = startedAt
      ? startOfUtcDay(startedAt)
      : startOfUtcDay(
          sessions[0]?.startedAt ? sessions[0].startedAt : now
        );
    const rangeEndExclusive = endedAt
      ? startOfUtcDay(endedAt)
      : new Date(startOfUtcDay(now).getTime() + DAY_MS);

    const daysInRange = Math.max(
      0,
      Math.round((rangeEndExclusive.getTime() - rangeStart.getTime()) / DAY_MS)
    );
    const daysPlayed = daySet.size;
    const daysNotPlayed = Math.max(0, daysInRange - daysPlayed);

    const averageSessionsPerDay =
      daysInRange > 0 ? totalSessions / daysInRange : 0;
    const averageSessionsPerWeek =
      daysInRange > 0 ? totalSessions / (daysInRange / 7) : 0;
    const monthsInRange = countMonthsInRange(rangeStart, rangeEndExclusive);
    const averageSessionsPerMonth =
      monthsInRange > 0 ? totalSessions / monthsInRange : 0;

    const sessionsByWeekday = weekdayCounts.map((count, index) => ({
      weekday: index,
      sessionCount: count,
    }));

    const sessionsByHour = hourCounts.map((count, hour) => ({
      hour,
      sessionCount: count,
    }));

    const peakHour =
      totalSessions > 0
        ? (() => {
            let best: { hour: number; sessionCount: number } | null = null;
            sessionsByHour.forEach((item) => {
              if (!best || item.sessionCount > best.sessionCount) {
                best = { hour: item.hour, sessionCount: item.sessionCount };
              }
            });
            return best;
          })()
        : null;

    const groupTotals = new Map<
      number,
      {
        representativeId: number;
        representativeTitle: string;
        representativeSeconds: number;
        totalSeconds: number;
        sessionCount: number;
      }
    >();

    gameTotals.forEach((game, gameId) => {
      const root = connectionIndex.find(gameId);
      const entry = groupTotals.get(root);
      if (!entry) {
        groupTotals.set(root, {
          representativeId: gameId,
          representativeTitle: game.title,
          representativeSeconds: game.totalSeconds,
          totalSeconds: game.totalSeconds,
          sessionCount: game.sessionCount,
        });
        return;
      }
      entry.totalSeconds += game.totalSeconds;
      entry.sessionCount += game.sessionCount;
      if (game.totalSeconds > entry.representativeSeconds) {
        entry.representativeId = gameId;
        entry.representativeTitle = game.title;
        entry.representativeSeconds = game.totalSeconds;
      }
    });

    const groupEntries = Array.from(groupTotals.values()).map((entry) => ({
      gameId: entry.representativeId,
      title: entry.representativeTitle,
      totalSeconds: entry.totalSeconds,
      sessionCount: entry.sessionCount,
      averageSessionSeconds:
        entry.sessionCount > 0 ? entry.totalSeconds / entry.sessionCount : 0,
    }));

    const groupRepresentatives = new Map(
      Array.from(groupTotals.entries()).map(([root, entry]) => [
        root,
        { gameId: entry.representativeId, title: entry.representativeTitle },
      ])
    );

    const topGamesByTime = groupEntries
      .slice()
      .sort((a, b) => b.totalSeconds - a.totalSeconds);

    const topGameByTime = topGamesByTime[0] ?? null;

    const topGameBySessions = groupEntries
      .slice()
      .sort((a, b) =>
        b.sessionCount === a.sessionCount
          ? b.totalSeconds - a.totalSeconds
          : b.sessionCount - a.sessionCount
      )[0] ?? null;

    const topGameByAverageSession = groupEntries
      .slice()
      .sort((a, b) =>
        b.averageSessionSeconds === a.averageSessionSeconds
          ? b.totalSeconds - a.totalSeconds
          : b.averageSessionSeconds - a.averageSessionSeconds
      )[0] ?? null;

    const uniqueGames = groupEntries.length;

    const timeOnTop3Percent =
      totalSeconds > 0
        ? (topGamesByTime
            .slice(0, 3)
            .reduce((sum, item) => sum + item.totalSeconds, 0) /
            totalSeconds) *
          100
        : 0;

    const abandonedGames = groupEntries.filter(
      (entry) => entry.sessionCount === 1
    ).length;

    const returnRateGames = Array.from(groupMonths.values()).filter(
      (set) => set.size > 1
    ).length;

    const mostBingedGame = (() => {
      let best: { root: number; sessionCount: number } | null = null;
      groupSessionTimes.forEach((times, root) => {
        if (times.length === 0) {
          return;
        }
        const sorted = times.slice().sort((a, b) => a - b);
        let maxCount = 1;
        let left = 0;
        for (let right = 0; right < sorted.length; right += 1) {
          while (sorted[right] - sorted[left] > DAY_MS) {
            left += 1;
          }
          maxCount = Math.max(maxCount, right - left + 1);
        }
        if (!best || maxCount > best.sessionCount) {
          best = { root, sessionCount: maxCount };
        }
      });
      if (!best) {
        return null;
      }
      const rep = groupRepresentatives.get(best.root);
      if (!rep) {
        return null;
      }
      return { gameId: rep.gameId, title: rep.title, sessionCount: best.sessionCount };
    })();

    const gameWithLongestSession = (() => {
      if (!longestSession) {
        return null;
      }
      const rep = groupRepresentatives.get(longestSession.root);
      if (!rep) {
        return null;
      }
      return {
        gameId: rep.gameId,
        title: rep.title,
        sessionSeconds: longestSession.seconds,
      };
    })();

    const firstSessionRows = await recapRepository.listFirstSessionsByGame(userId);
    const firstSessions = await mapSessionsForUser(
      userId,
      firstSessionRows.map(toDomainFirstSession)
    );
    const firstByGroup = new Map<number, Date>();
    firstSessions.forEach((session) => {
      const root = connectionIndex.find(session.gameId);
      const existing = firstByGroup.get(root);
      if (!existing || session.startedAt < existing) {
        firstByGroup.set(root, session.startedAt);
      }
    });
    const newGamesTried = Array.from(firstByGroup.values()).filter(
      (date) => date >= rangeStart && date < rangeEndExclusive
    ).length;

    const platformTotalsList = Array.from(platformTotals.values()).sort(
      (a, b) => b.totalSeconds - a.totalSeconds
    );

    const mostUsedPlatform = platformTotalsList.length
      ? {
          platform: platformTotalsList[0].platform,
          totalSeconds: platformTotalsList[0].totalSeconds,
          sessionCount: platformTotalsList[0].sessionCount,
        }
      : null;

    return {
      range: { from, to },
      totalSessions,
      totalSeconds,
      averageSessionSeconds,
      medianSessionSeconds,
      longestSessionSeconds,
      shortestSessionSeconds,
      averageSessionsPerDay,
      averageSessionsPerWeek,
      averageSessionsPerMonth,
      mostActiveDay,
      mostActiveMonth,
      longestStreakDays,
      daysPlayed,
      daysNotPlayed,
      sessionsByWeekday,
      sessionsByHour,
      peakHour,
      weekendSeconds,
      weekdaySeconds,
      uniqueGames,
      topGameBySessions,
      topGameByTime,
      topGamesByTime: topGamesByTime.slice(0, 5),
      topGameByAverageSession,
      gameWithLongestSession,
      newGamesTried,
      abandonedGames,
      returnRateGames,
      timeOnTop3Percent,
      mostBingedGame,
      platformTotals: platformTotalsList,
      mostUsedPlatform,
      gamesTable: topGamesByTime,
    };
  },
};
