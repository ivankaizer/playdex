export type PlayniteSessionResponseDto = {
  sessionId: number;
};

export type PlayniteSessionStartResponseDto = {
  sessionId: number;
};

export type PlayniteSessionEndResponseDto = {
  endedSessionCount: number;
};

export type PlayniteSessionPingResponseDto = {
  sessionId: number;
  started: boolean;
  endedSessionCount: number;
};
