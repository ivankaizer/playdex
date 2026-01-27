export type AccountSession = {
  sessionId: number;
  startedAt: string;
  endedAt: string | null;
  gameId: string;
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
  gameId: string;
  ignored: boolean;
};

export type GameRatingResponse = {
  gameId: string;
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
  topGames: { gameId: string; title: string; totalSeconds: number }[];
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
  mappedGameId: string | null;
  mappedSource: "local" | "igdb" | null;
  mappedGame: {
    id: string;
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
  gameId?: string;
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
  mappedGameId: string | null;
};

export type ManualEntryCreateResponse = {
  sessionId: number;
};

export type Game = {
  id: string;
  title: string;
  platform: string;
  capsuleUrl: string | null;
};

export type GameSearchResult = {
  gameId: string;
  title: string;
  platform: string;
  capsuleUrl: string | null;
};

export type GameSearchResponse = {
  query: string;
  results: GameSearchResult[];
};

export type GameConnectionsResponse = {
  connections: {
    gameId: string;
    title: string;
    platform: string;
    capsuleUrl: string | null;
    connectedGameId: string;
    connectedTitle: string;
    connectedPlatform: string;
    connectedCapsuleUrl: string | null;
  }[];
};

export type GameConnectionChangeResponse = {
  gameId: string;
  connectedGameId: string;
};

export type NavItem = {
  label: string;
  path: string;
};
