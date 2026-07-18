export type GameIgnoreResponseDto = {
  gameId: string;
  ignored: boolean;
};

export type GameRatingResponseDto = {
  gameId: string;
  rating: number | null;
};

export type GameResponseDto = {
  id: string;
  title: string;
  platform: string;
  capsuleUrl: string | null;
};

export type GameSearchResultDto = {
  gameId: string;
  title: string;
  platform: string;
  capsuleUrl: string | null;
};

export type GameSearchResponseDto = {
  query: string;
  results: GameSearchResultDto[];
};

export type GameConnectionDto = {
  gameId: string;
  title: string;
  platform: string;
  capsuleUrl: string | null;
  connectedGameId: string;
  connectedTitle: string;
  connectedPlatform: string;
  connectedCapsuleUrl: string | null;
};

export type GameConnectionListResponseDto = {
  connections: GameConnectionDto[];
};

export type GameConnectionChangeResponseDto = {
  gameId: string;
  connectedGameId: string;
};
