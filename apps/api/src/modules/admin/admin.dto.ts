export type AdminRecentSessionDto = {
  sessionId: number;
  gameName: string;
  startedAt: Date;
  durationSeconds: number;
  live: boolean;
};

export type AdminUserRecentSessionsDto = {
  userId: number;
  email: string;
  sessions: AdminRecentSessionDto[];
};

export type AdminRecentSessionsResponseDto = {
  users: AdminUserRecentSessionsDto[];
};

export type AdminMovableSessionDto = {
  sessionId: number;
  sourceCredentialId: number;
  userEmail: string;
  platform: string;
  accountUser: string | null;
  gameName: string;
  startedAt: Date;
  endedAt: Date | null;
  durationSeconds: number;
  live: boolean;
};

export type AdminSessionsListResponseDto = {
  query: string;
  sessions: AdminMovableSessionDto[];
  total: number;
  limit: number;
  offset: number;
};

export type AdminPlatformStatusDto = {
  platform: string;
  totalCredentials: number;
  activeCredentials: number;
  enabled: boolean;
};

export type AdminPlatformStatusesResponseDto = {
  platforms: AdminPlatformStatusDto[];
};

export type AdminPlatformStatusUpdateResponseDto = {
  platform: string;
  enabled: boolean;
  updatedCredentials: number;
};

export type AdminCredentialDto = {
  id: number;
  userEmail: string;
  platform: string;
  apiKey: string | null;
  user: string | null;
  active: boolean;
};

export type AdminCredentialsResponseDto = {
  credentials: AdminCredentialDto[];
};

export type AdminCredentialUpdateResponseDto = {
  credential: AdminCredentialDto;
};

export type AdminSessionMoveResponseDto = {
  movedSessions: number;
  targetCredentialId: number;
};

export type AdminGameDto = {
  id: string;
  title: string;
  titleLocked: boolean;
  platform: string;
  imageUrl: string | null;
  imageLocked: boolean;
};

export type AdminGameSearchResponseDto = {
  query: string;
  games: AdminGameDto[];
};

export type AdminGameUpdateResponseDto = {
  game: AdminGameDto;
};
