export interface GameSearchResult {
  gameId: string;
  title: string;
  platform: string;
  capsuleUrl: string | null;
}

export interface GameSearchResponse {
  query: string;
  results: GameSearchResult[];
}

export interface GameResponse {
  id: string;
  title: string;
  platform: string;
  capsuleUrl: string | null;
}

export interface GameConnection {
  gameId: string;
  title: string;
  platform: string;
  capsuleUrl: string | null;
  connectedGameId: string;
  connectedTitle: string;
  connectedPlatform: string;
  connectedCapsuleUrl: string | null;
}

export interface GameConnectionsResponse {
  connections: GameConnection[];
}

export interface GameConnectionChangeRequest {
  gameId: string;
  connectedGameId: string;
}

export interface GameConnectionChangeResponse {
  gameId: string;
  connectedGameId: string;
}

export interface GameIgnoreRequest {
  gameId: string;
}

export interface GameIgnoreResponse {
  gameId: string;
  ignored: boolean;
}

export interface GameRatingRequest {
  gameId: string;
  rating: number | null;
}

export interface GameRatingResponse {
  gameId: string;
  rating: number | null;
}

export interface GameYearStat {
  year: number;
  seconds: number;
}

export interface GameMonthStat {
  year: number;
  month: number;
  seconds: number;
}

export interface GameStatsResponse {
  totalSeconds: number;
  sessionCount: number;
  lastPlayedAt: string | null;
  years: GameYearStat[];
  months: GameMonthStat[];
}
