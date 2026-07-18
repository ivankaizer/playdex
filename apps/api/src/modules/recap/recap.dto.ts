export type RecapYearsResponseDto = {
  years: number[];
};

export type RecapGameStatDto = {
  gameId: string;
  title: string;
  capsuleUrl: string | null;
  totalSeconds: number;
  sessionCount: number;
  averageSessionSeconds: number;
  isAbandoned: boolean;
};

export type RecapPlatformStatDto = {
  platform: string;
  sessionCount: number;
  totalSeconds: number;
  source?: string | null;
};

export type RecapResponseDto = {
  range: { from: string | null; to: string | null };
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
  topGameBySessions: RecapGameStatDto | null;
  topGameByTime: RecapGameStatDto | null;
  topGamesByTime: RecapGameStatDto[];
  topGameByAverageSession: RecapGameStatDto | null;
  gameWithLongestSession: {
    gameId: string;
    title: string;
    sessionSeconds: number;
  } | null;
  newGamesTried: number;
  abandonedGames: number;
  returnRateGames: number;
  timeOnTop3Percent: number;
  mostBingedGame: {
    gameId: string;
    title: string;
    sessionCount: number;
  } | null;
  platformTotals: RecapPlatformStatDto[];
  mostUsedPlatform: {
    platform: string;
    totalSeconds: number;
    sessionCount: number;
  } | null;
  gamesTable: RecapGameStatDto[];
};
