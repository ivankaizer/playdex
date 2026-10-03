export interface AccountPlatformStat {
  platformKey: string;
  platformName: string;
  totalSeconds: number;
  source: string | null;
}

export interface AccountTopGame {
  gameId: string;
  title: string;
  totalSeconds: number;
}

export interface AccountMonthStat {
  year: number;
  month: number;
  totalSeconds: number;
}

export interface AccountStatsPeriod {
  totalSeconds: number;
  sessionCount: number;
  topGameTitle: string | null;
  platforms: AccountPlatformStat[];
  topGames: AccountTopGame[];
  months: AccountMonthStat[];
}

export interface AccountStatsRangeResponse {
  accountId: number | null;
  stats: AccountStatsPeriod;
}
