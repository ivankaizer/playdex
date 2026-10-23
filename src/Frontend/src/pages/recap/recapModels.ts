export interface RecapYearsResponse {
  years: number[];
}

export interface RecapGameStat {
  gameId: string;
  title: string;
  capsuleUrl: string | null;
  totalSeconds: number;
  sessionCount: number;
  averageSessionSeconds: number;
  isAbandoned: boolean;
}

export interface RecapPlatformStat {
  platformKey: string;
  platformName: string;
  sessionCount: number;
  totalSeconds: number;
}

export interface MostActiveDay {
  date: string;
  sessionCount: number;
}

export interface MostActiveMonth {
  month: string;
  sessionCount: number;
}

export interface WeekdayStat {
  weekday: number;
  sessionCount: number;
}

export interface HourStat {
  hour: number;
  sessionCount: number;
}

export interface LongestSessionGame {
  gameId: string;
  title: string;
  sessionSeconds: number;
}

export interface BingedGame {
  gameId: string;
  title: string;
  sessionCount: number;
}

export interface PlatformSummary {
  platformKey: string;
  platformName: string;
  totalSeconds: number;
  sessionCount: number;
}

export interface RecapRange {
  from: string | null;
  to: string | null;
}

export interface RecapResponse {
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
  mostActiveDay: MostActiveDay | null;
  mostActiveMonth: MostActiveMonth | null;
  longestStreakDays: number;
  daysPlayed: number;
  daysNotPlayed: number;
  sessionsByWeekday: WeekdayStat[];
  sessionsByHour: HourStat[];
  peakHour: HourStat | null;
  weekendSeconds: number;
  weekdaySeconds: number;
  uniqueGames: number;
  topGameBySessions: RecapGameStat | null;
  topGameByTime: RecapGameStat | null;
  topGamesByTime: RecapGameStat[];
  topGameByAverageSession: RecapGameStat | null;
  gameWithLongestSession: LongestSessionGame | null;
  newGamesTried: number;
  abandonedGames: number;
  returnRateGames: number;
  topThreeGamesSharePercent: number;
  mostBingedGame: BingedGame | null;
  platformTotals: RecapPlatformStat[];
  mostUsedPlatform: PlatformSummary | null;
  gamesTable: RecapGameStat[];
}
