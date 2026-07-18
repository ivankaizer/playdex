export type AccountSummaryDto = {
  id: number;
  name: string;
};

export type AccountSessionDto = {
  sessionId: number;
  startedAt: Date;
  endedAt: Date | null;
  gameId: string;
  platform: string;
  game: string;
  capsuleUrl: string | null;
  ignored: boolean;
  mappedPlatform?: string;
  mappedSource?: "local" | "igdb";
};

export type AccountSessionsResponseDto = {
  accountId: number | null;
  sessions: AccountSessionDto[];
  total: number;
  limit: number;
  offset: number;
};

export type AccountTodaySessionsResponseDto = {
  accountId: number | null;
  sessions: AccountSessionDto[];
};

export type AccountCurrentSessionResponseDto = {
  accountId: number | null;
  session: AccountSessionDto | null;
};

export type AccountSessionDeleteResponseDto = {
  sessionId: number;
};

export type AccountStatsPeriodDto = {
  totalSeconds: number;
  sessionCount: number;
  topGameTitle: string | null;
  platforms: {
    platform: string;
    totalSeconds: number;
    source?: string | null;
  }[];
  topGames: { gameId: string; title: string; totalSeconds: number }[];
};

export type AccountStatsResponseDto = {
  accountId: number | null;
  monthly: AccountStatsPeriodDto;
  yearly: AccountStatsPeriodDto;
};

export type AccountStatsRangeResponseDto = {
  accountId: number | null;
  stats: AccountStatsPeriodDto;
};
