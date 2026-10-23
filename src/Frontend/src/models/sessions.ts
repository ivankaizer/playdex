/** Timestamps are ISO-8601 strings as serialized by the API. */
export interface AccountSession {
  sessionId: number;
  startedAt: string;
  endedAt: string | null;
  gameId: string;
  platformKey: string;
  platformName: string;
  game: string;
  capsuleUrl: string | null;
  ignored: boolean;
}

export interface AccountSessionsResponse {
  accountId: number | null;
  sessions: AccountSession[];
  total: number;
  limit: number;
  offset: number;
}

export interface AccountTodaySessionsResponse {
  accountId: number | null;
  /** The newest of today's sessions (at most 10). */
  sessions: AccountSession[];
  /** All of today's sessions, listed or not. */
  total: number;
}

export interface AccountCurrentSessionResponse {
  accountId: number | null;
  session: AccountSession | null;
}

export interface AccountSessionUpdateRequest {
  startedAt: string;
  endedAt: string;
}

export interface AccountSessionUpdateResponse {
  sessionId: number;
  startedAt: string;
  endedAt: string;
}
