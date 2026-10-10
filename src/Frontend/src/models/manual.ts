export interface ManualSessionStartRequest {
  title: string | null;
  gameId: string | null;
  platformKey: string | null;
  igdbId: string | null;
  capsuleUrl: string | null;
}

export interface ManualEntryCreateRequest {
  date: string;
  durationMinutes: number;
  gameId: string | null;
  igdbId: string | null;
  title: string | null;
  platformKey: string | null;
  capsuleUrl: string | null;
}

export interface ManualSessionStartResponse {
  sessionId: number;
}

export interface ManualSessionStopResponse {
  endedSessionCount: number;
}

export interface ManualEntryCreateResponse {
  sessionId: number;
}

export interface ManualMappingSearchResult {
  source: string;
  gameId: string | null;
  igdbId: string | null;
  title: string;
  platform: string | null;
  platformId: number | null;
  capsuleUrl: string | null;
}

export interface ManualMappingSearchResponse {
  query: string;
  igdbAvailable: boolean;
  results: ManualMappingSearchResult[];
}

export interface ManualPlatform {
  key: string;
  name: string;
}

export interface ManualPlatformsResponse {
  platforms: ManualPlatform[];
}

export interface ManualActiveSession {
  sessionId: number;
  gameId: string;
  title: string;
  platformKey: string;
  platformName: string;
  startedAt: string;
  capsuleUrl: string | null;
}

export interface ManualActiveSessionResponse {
  session: ManualActiveSession | null;
}
