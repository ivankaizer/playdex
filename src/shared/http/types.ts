import type { MetricsSnapshot } from "../observability";

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
  gameId: number;
  platform: string;
  game: string;
  capsuleUrl: string | null;
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
  gameId: number;
  ignored: boolean;
};

export type GameRatingResponseDto = {
  gameId: number;
  rating: number | null;
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
  topGames: { title: string; totalSeconds: number }[];
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
  mappedGameId: number | null;
  mappedSource: "local" | "igdb" | null;
  mappedGame: {
    id: number;
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
  gameId?: number;
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
  mappedGameId: number | null;
};

export type ManualEntryCreateResponseDto = {
  sessionId: number;
};
