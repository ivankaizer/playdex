import type { ManualMappingSearchResult } from "./manual";

export interface DraftCapabilities {
  available: boolean;
  provider: string;
  maxTextCharacters: number;
  maxImageBytes: number;
  maxImagePixels: number;
  maxRows: number;
  historyRetentionDays?: number;
}

export interface DraftRowDto {
  id: string;
  revision: number;
  status: string;
  title: string;
  date: string | null;
  durationMinutes: number | null;
  platformKey: string;
  selection: ManualMappingSearchResult | null;
  warnings: string[];
  ready: boolean;
  acceptedSessionId: number | null;
}

export interface DraftBatchDto {
  id: string;
  createdAt: string;
  rows: DraftRowDto[];
}

export interface DraftRowUpdate {
  revision: number;
  title: string;
  date: string | null;
  durationMinutes: number | null;
  platformKey: string;
  gameId: string | null;
  igdbId: string | null;
}

export interface DraftAcceptRow {
  id: string;
  revision: number;
}

export interface DraftAcceptRequest {
  rows: DraftAcceptRow[];
}

export interface DraftAcceptResult {
  id: string;
  sessionId: number | null;
  error: string | null;
}

export interface DraftAcceptResponse {
  results: DraftAcceptResult[];
}
