export type AccountSession = {
  sessionId: number;
  startedAt: string;
  endedAt: string | null;
  gameId: number;
  platform: string;
  game: string;
  capsuleUrl: string | null;
  ignored: boolean;
  mappedPlatform?: string;
  mappedSource?: "local" | "igdb";
};

export type AccountSessionsResponse = {
  accountId: number | null;
  sessions: AccountSession[];
  total: number;
  limit: number;
  offset: number;
};

export type AccountTodaySessionsResponse = {
  accountId: number | null;
  sessions: AccountSession[];
};

export type AccountCurrentSessionResponse = {
  accountId: number | null;
  session: AccountSession | null;
};

export type GameIgnoreResponse = {
  gameId: number;
  ignored: boolean;
};

export type GameRatingResponse = {
  gameId: number;
  rating: number | null;
};

export type UserSettingsResponse = {
  ignoreShortSessionsMinutes: number;
};

export type AccountStatsPeriod = {
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

export type AccountStatsRangeResponse = {
  accountId: number | null;
  stats: AccountStatsPeriod;
};

export type ProviderStatus = {
  id: number;
  platform: string;
  accountId: string | null;
  status: "active" | "paused";
  apiKey: string | null;
};

export type ProvidersResponse = {
  providers: ProviderStatus[];
};

export type PlayniteKeyResponse = {
  apiKey: string;
};

export type ManualInput = {
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

export type ManualInputsResponse = {
  inputs: ManualInput[];
};

export type ManualSessionStartResponse = {
  sessionId: number;
};

export type ManualSessionStopResponse = {
  endedSessionCount: number;
};

export type ManualMappingSearchResult = {
  source: "local" | "igdb";
  gameId?: number;
  igdbId?: string;
  title: string;
  platform: string | null;
  platformId?: number | null;
  capsuleUrl: string | null;
};

export type ManualMappingSearchResponse = {
  query: string;
  igdbAvailable: boolean;
  results: ManualMappingSearchResult[];
};

export type ManualMappingResponse = {
  inputId: number;
  mappedGameId: number | null;
};

export type ManualEntryCreateResponse = {
  sessionId: number;
};

export type NavItem = {
  label: string;
  path: string;
};
