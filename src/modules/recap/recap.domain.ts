export type RecapRange = {
  from: string | null;
  to: string | null;
};

export type RecapGameStat = {
  gameId: number;
  title: string;
  totalSeconds: number;
  sessionCount: number;
  averageSessionSeconds: number;
};

export type RecapPlatformStat = {
  platform: string;
  sessionCount: number;
  totalSeconds: number;
  source?: string | null;
};

export type RecapStats = {
  range: RecapRange;
  totalSessions: number;
  totalSeconds: number;
  averageSessionSeconds: number;
  medianSessionSeconds: number;
  longestSessionSeconds: number;
  shortestSessionSeconds: number;
  averageSessionsPerDay: number;
  averageSessionsPerWeek: number;
  averageSessionsPerMonth: number;
  mostActiveDay: { date: string; sessionCount: number } | null;
  mostActiveMonth: { month: string; sessionCount: number } | null;
  longestStreakDays: number;
  daysPlayed: number;
  daysNotPlayed: number;
  sessionsByWeekday: { weekday: number; sessionCount: number }[];
  sessionsByHour: { hour: number; sessionCount: number }[];
  peakHour: { hour: number; sessionCount: number } | null;
  weekendSeconds: number;
  weekdaySeconds: number;
  uniqueGames: number;
  topGameBySessions: RecapGameStat | null;
  topGameByTime: RecapGameStat | null;
  topGamesByTime: RecapGameStat[];
  topGameByAverageSession: RecapGameStat | null;
  gameWithLongestSession: {
    gameId: number;
    title: string;
    sessionSeconds: number;
  } | null;
  newGamesTried: number;
  abandonedGames: number;
  returnRateGames: number;
  timeOnTop3Percent: number;
  mostBingedGame: { gameId: number; title: string; sessionCount: number } | null;
  platformTotals: RecapPlatformStat[];
  mostUsedPlatform: {
    platform: string;
    totalSeconds: number;
    sessionCount: number;
  } | null;
  gamesTable: RecapGameStat[];
};
