export type ManualInputDto = {
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

export type ManualInputsResponseDto = {
  inputs: ManualInputDto[];
};

export type ManualSessionStartResponseDto = {
  sessionId: number;
};

export type ManualSessionStopResponseDto = {
  endedSessionCount: number;
};

export type ManualMappingSearchResultDto = {
  source: "local" | "igdb";
  gameId?: string;
  igdbId?: string;
  title: string;
  platform: string | null;
  platformId?: number | null;
  capsuleUrl: string | null;
};

export type ManualMappingSearchResponseDto = {
  query: string;
  igdbAvailable: boolean;
  results: ManualMappingSearchResultDto[];
};

export type ManualMappingResponseDto = {
  inputId: number;
  mappedGameId: string | null;
};

export type ManualEntryCreateResponseDto = {
  sessionId: number;
};
