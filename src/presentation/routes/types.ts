import type { MetricsSnapshot } from "../../shared/observability";

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
  platform: string;
  game: string;
  capsuleUrl: string | null;
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

export type AccountStatsPeriodDto = {
  totalSeconds: number;
  sessionCount: number;
  topGameTitle: string | null;
  platforms: { platform: string; totalSeconds: number }[];
  topGames: { title: string; totalSeconds: number }[];
};

export type AccountStatsResponseDto = {
  accountId: number | null;
  monthly: AccountStatsPeriodDto;
  yearly: AccountStatsPeriodDto;
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
