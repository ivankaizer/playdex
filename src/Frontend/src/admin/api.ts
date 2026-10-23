import { ApiError, apiBlob, apiRequest, isAbortError } from "@/lib/api";
import { authSession } from "@/lib/auth";

// ---- Models (mirror src/Api/Modules/Admin/AdminDto.cs) -----------------------------------

export type CaptureSource = "polling" | "api" | "manual";

export interface DashboardCounts {
  liveSessions: number;
  liveUsers: number;
  sessionsToday: number;
  aiErrors: number;
}

export interface DashboardPlatform {
  key: string;
  name: string;
}

export interface DashboardSession {
  id: number;
  email: string;
  game: string;
  capsuleUrl: string | null;
  platformKey: string;
  platform: string;
  accountId: string | null;
  source: CaptureSource;
  startedAt: string;
  endedAt: string | null;
  lastPolledAt: string | null;
}

export interface DashboardGeneration {
  id: string;
  email: string;
  model: string;
  status: string;
  createdAt: string;
}

export interface DashboardSnapshot {
  updatedAt: string;
  counts: DashboardCounts;
  platforms: DashboardPlatform[];
  liveSessions: DashboardSession[];
  endedSessions: DashboardSession[];
  failedGenerations: DashboardGeneration[];
}

export interface PlatformSummary {
  key: string;
  name: string;
  total: number;
  active: number;
  enabled: boolean;
}

export interface IntegrationSummary {
  id: number;
  email: string;
  platform: string;
  accountId: string | null;
  active: boolean;
  apiKey: string | null;
}

export interface SessionSummary {
  id: number;
  email: string;
  platform: string;
  accountId: string | null;
  game: string;
  startedAt: string;
  endedAt: string | null;
  live: boolean;
}

export interface SessionPage {
  sessions: SessionSummary[];
  total: number;
  page: number;
  pageSize: number;
}

export interface GameSummary {
  id: number;
  title: string;
  capsuleUrl: string | null;
  titleLocked: boolean;
  capsuleUrlLocked: boolean;
}

export interface GameUpdate {
  title: string | null;
  titleChanged: boolean;
  capsuleUrl: string | null;
  capsuleUrlChanged: boolean;
  titleLocked: boolean | null;
  capsuleUrlLocked: boolean | null;
}

export interface IntegrationUpdate {
  apiKeyChanged: boolean;
  apiKey: string | null;
  accountIdChanged: boolean;
  accountId: string | null;
}

export interface GenerationSummary {
  id: string;
  email: string;
  createdAt: string;
  provider: string;
  model: string;
  status: string;
  inputPreview: string | null;
  hasImage: boolean;
}

export interface GenerationPage {
  items: GenerationSummary[];
  total: number;
  page: number;
  pageSize: number;
}

export interface GenerationResultRow {
  title: string;
  date: string | null;
  durationMinutes: number | null;
  platformKey: string;
}

export interface GenerationResponse {
  httpStatus: number;
  body: string;
  truncated: boolean;
}

export interface GenerationDetail {
  id: string;
  email: string;
  requestId: string;
  createdAt: string;
  completedAt: string | null;
  provider: string;
  model: string;
  timezone: string;
  referenceDate: string;
  inputText: string | null;
  hasImage: boolean;
  status: string;
  error: string | null;
  result: string | null;
  rows: GenerationResultRow[];
  responses: GenerationResponse[];
}

// ---- Calls ------------------------------------------------------------------------------------

/** A token that cannot be refreshed means the admin session is over: start again at the login page. */
export function adminAuthFailure(): void {
  authSession.logout();
  window.location.assign("/admin/login");
}

interface CallOptions {
  signal?: AbortSignal;
}

function query(params: Record<string, string | number | null | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === null || value === undefined || value === "") continue;
    search.set(key, String(value));
  }
  const text = search.toString();
  return text ? `?${text}` : "";
}

function get<T>(path: string, options: CallOptions = {}): Promise<T> {
  return apiRequest<T>(path, { signal: options.signal, onAuthFailure: adminAuthFailure });
}

function send<T>(method: "POST" | "PUT" | "PATCH" | "DELETE", path: string, body?: unknown): Promise<T> {
  return apiRequest<T>(path, { method, body, onAuthFailure: adminAuthFailure });
}

export const adminApi = {
  dashboard(params: { query: string; platform: string | null; dayStart: string; dayEnd: string }, options?: CallOptions) {
    return get<DashboardSnapshot>(`admin/dashboard${query(params)}`, options);
  },
  platforms(options?: CallOptions) {
    return get<PlatformSummary[]>("admin/platforms", options);
  },
  setPlatformEnabled(key: string, enabled: boolean) {
    return send<{ updated: number }>("PUT", `admin/platforms/${encodeURIComponent(key)}`, { enabled });
  },
  integrations(options?: CallOptions) {
    return get<IntegrationSummary[]>("admin/integrations", options);
  },
  updateIntegration(id: number, update: IntegrationUpdate) {
    return send<void>("PATCH", `admin/integrations/${id}`, update);
  },
  sessions(params: { query: string; page: number; status: string | null; from: string | null; to: string | null }, options?: CallOptions) {
    return get<SessionPage>(`admin/sessions${query(params)}`, options);
  },
  transferSessions(sessionIds: number[], targetIntegrationId: number) {
    return send<{ transferred: number }>("POST", "admin/sessions/transfer", { sessionIds, targetIntegrationId });
  },
  deleteSessions(sessionIds: number[]) {
    return send<{ deleted: number }>("POST", "admin/sessions/delete", { sessionIds });
  },
  games(search: string, options?: CallOptions) {
    return get<GameSummary[]>(`admin/games${query({ query: search.trim() })}`, options);
  },
  updateGame(id: number, update: GameUpdate) {
    return send<void>("PATCH", `admin/games/${id}`, update);
  },
  mergeGames(sourceId: number, targetId: number) {
    return send<void>("POST", "admin/games/merge", { sourceId, targetId });
  },
  generations(
    params: { query: string; status: string | null; page: number; since: string | null; until: string | null },
    options?: CallOptions,
  ) {
    return get<GenerationPage>(`admin/ai-generations${query(params)}`, options);
  },
  generation(id: string, options?: CallOptions) {
    return get<GenerationDetail>(`admin/ai-generations/${encodeURIComponent(id)}`, options);
  },
  generationImage(id: string, options?: CallOptions) {
    return apiBlob(`admin/ai-generations/${encodeURIComponent(id)}/image`, {
      signal: options?.signal,
      onAuthFailure: adminAuthFailure,
    });
  },
  deleteGeneration(id: string) {
    return send<void>("DELETE", `admin/ai-generations/${encodeURIComponent(id)}`);
  },
};

export const AdminAccessRequired = "Administrator access is required.";

export function isForbidden(error: unknown): boolean {
  return error instanceof ApiError && error.status === 403;
}

export function isNotFound(error: unknown): boolean {
  return error instanceof ApiError && error.status === 404;
}

export { isAbortError };
