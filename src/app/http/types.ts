import type { MetricsSnapshot } from "../../common/observability";

export type ApiErrorResponse = {
  error: string;
};

export type ApiResponse<T> =
  | { data: T; error: null }
  | { data: null; error: ApiErrorResponse };

export type HealthResponseDto = {
  ok: true;
};

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

export type GameIgnoreResponseDto = {
  gameId: string;
  ignored: boolean;
};

export type GameRatingResponseDto = {
  gameId: string;
  rating: number | null;
};

export type UserSettingsResponseDto = {
  ignoreShortSessionsMinutes: number;
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
  mostBingedGame: { gameId: string; title: string; sessionCount: number } | null;
  platformTotals: RecapPlatformStatDto[];
  mostUsedPlatform: {
    platform: string;
    totalSeconds: number;
    sessionCount: number;
  } | null;
  gamesTable: RecapGameStatDto[];
};

export type ProviderStatusDto = {
  id: number;
  platform: string;
  accountId: string | null;
  status: "active" | "paused";
  apiKey: string | null;
};

export type ProvidersResponseDto = {
  providers: ProviderStatusDto[];
};

export type ProviderUpdateResponseDto = {
  provider: ProviderStatusDto;
};

export type PlayniteKeyResponseDto = {
  apiKey: string;
};

export type MetricsResponseDto = MetricsSnapshot;

export type AccountSessionsParams = {
  accountId: string;
};

export type PlayniteSessionResponseDto = {
  sessionId: number;
};

export type PlayniteSessionStartResponseDto = {
  sessionId: number;
};

export type PlayniteSessionEndResponseDto = {
  endedSessionCount: number;
};

export type ManualInputDto = {
  id: number;
  input: string;
  canonicalTitle: string;
  mappedGameId: string | null;
  mappedSource: "local" | "igdb" | null;
  mappedGame: {
    id: string;
    title: string;
    platform: string;
    capsuleUrl: string | null;
  } | null;
};

export type ManualInputsResponseDto = {
  inputs: ManualInputDto[];
};

export type ManualSessionStartResponseDto = {
  sessionId: number;
};

export type ManualSessionStopResponseDto = {
  endedSessionCount: number;
};

export type ManualMappingSearchResultDto = {
  source: "local" | "igdb";
  gameId?: string;
  igdbId?: string;
  title: string;
  platform: string | null;
  platformId?: number | null;
  capsuleUrl: string | null;
};

export type ManualMappingSearchResponseDto = {
  query: string;
  igdbAvailable: boolean;
  results: ManualMappingSearchResultDto[];
};

export type ManualMappingResponseDto = {
  inputId: number;
  mappedGameId: string | null;
};

export type ManualEntryCreateResponseDto = {
  sessionId: number;
};

export type GameResponseDto = {
  id: string;
  title: string;
  platform: string;
  capsuleUrl: string | null;
};

export type GameSearchResultDto = {
  gameId: string;
  title: string;
  platform: string;
  capsuleUrl: string | null;
};

export type GameSearchResponseDto = {
  query: string;
  results: GameSearchResultDto[];
};

export type GameConnectionDto = {
  gameId: string;
  title: string;
  platform: string;
  capsuleUrl: string | null;
  connectedGameId: string;
  connectedTitle: string;
  connectedPlatform: string;
  connectedCapsuleUrl: string | null;
};

export type GameConnectionListResponseDto = {
  connections: GameConnectionDto[];
};

export type GameConnectionChangeResponseDto = {
  gameId: string;
  connectedGameId: string;
};
