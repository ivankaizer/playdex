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

export type RecapYearsResponse = {
  years: number[];
};

export type RecapGameStat = {
  gameId: string;
  title: string;
  capsuleUrl: string | null;
  totalSeconds: number;
  sessionCount: number;
  averageSessionSeconds: number;
  isAbandoned: boolean;
};

export type RecapPlatformStat = {
  platform: string;
  sessionCount: number;
  totalSeconds: number;
  source?: string | null;
};

export type RecapResponse = {
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
  topGameBySessions: RecapGameStat | null;
  topGameByTime: RecapGameStat | null;
  topGamesByTime: RecapGameStat[];
  topGameByAverageSession: RecapGameStat | null;
  gameWithLongestSession: {
    gameId: string;
    title: string;
    sessionSeconds: number;
  } | null;
  newGamesTried: number;
  abandonedGames: number;
  returnRateGames: number;
  timeOnTop3Percent: number;
  mostBingedGame: {
    gameId: string;
    title: string;
    sessionCount: number;
  } | null;
  platformTotals: RecapPlatformStat[];
  mostUsedPlatform: {
    platform: string;
    totalSeconds: number;
    sessionCount: number;
  } | null;
  gamesTable: RecapGameStat[];
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

export type ProviderUpdateResponse = {
  provider: ProviderStatus;
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

export type LoginResponse = {
  userId: number;
  email: string;
  isAdmin: boolean;
  accessToken: string;
  refreshToken: string;
};

export type MeResponse = {
  userId: number;
  email: string;
  isAdmin: boolean;
};

export type AdminRecentSession = {
  sessionId: number;
  gameName: string;
  startedAt: string;
  durationSeconds: number;
  live: boolean;
};

export type AdminRecentSessionsUser = {
  userId: number;
  email: string;
  sessions: AdminRecentSession[];
};

export type AdminRecentSessionsResponse = {
  users: AdminRecentSessionsUser[];
};

export type AdminMovableSession = {
  sessionId: number;
  sourceCredentialId: number;
  userEmail: string;
  platform: string;
  accountUser: string | null;
  gameName: string;
  startedAt: string;
  endedAt: string | null;
  durationSeconds: number;
  live: boolean;
};

export type AdminSessionsListResponse = {
  query: string;
  sessions: AdminMovableSession[];
  total: number;
  limit: number;
  offset: number;
};

export type AdminPlatformStatus = {
  platform: string;
  totalCredentials: number;
  activeCredentials: number;
  enabled: boolean;
};

export type AdminPlatformStatusesResponse = {
  platforms: AdminPlatformStatus[];
};

export type AdminPlatformStatusUpdateResponse = {
  platform: string;
  enabled: boolean;
  updatedCredentials: number;
};

export type AdminCredential = {
  id: number;
  userEmail: string;
  platform: string;
  apiKey: string | null;
  user: string | null;
  active: boolean;
};

export type AdminCredentialsResponse = {
  credentials: AdminCredential[];
};

export type AdminCredentialUpdateResponse = {
  credential: AdminCredential;
};

export type AdminSessionMoveResponse = {
  movedSessions: number;
  targetCredentialId: number;
};

export type AdminGame = {
  id: string;
  title: string;
  titleLocked: boolean;
  platform: string;
  imageUrl: string | null;
  imageLocked: boolean;
};

export type AdminGameSearchResponse = {
  query: string;
  games: AdminGame[];
};

export type AdminGameUpdateResponse = {
  game: AdminGame;
};
