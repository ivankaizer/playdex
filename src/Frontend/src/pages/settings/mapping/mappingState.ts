import { encodeGameId } from "@/lib/gameHash";
import type { GameConnection, GameConnectionsResponse, GameSearchResponse, GameSearchResult } from "@/models/games";
import type { Submission } from "../models";

export type SearchSlot = "primary" | "secondary";

export interface GameSearchState {
  query: string;
  results: GameSearchResult[];
  error: string;
  isLoading: boolean;
  selected: GameSearchResult | null;
}

export interface MappingState {
  gameConnections: GameConnection[];
  gameConnectionsError: string;
  isGameConnectionsLoading: boolean;
  primary: GameSearchState;
  secondary: GameSearchState;
  isConnectionSaving: boolean;
  connectionSaveError: string;
}

export const gameConnectionsPath = "/games/connections";

function createSearchState(): GameSearchState {
  return { query: "", results: [], error: "", isLoading: false, selected: null };
}

export function createMappingState(): MappingState {
  return {
    gameConnections: [],
    gameConnectionsError: "",
    isGameConnectionsLoading: false,
    primary: createSearchState(),
    secondary: createSearchState(),
    isConnectionSaving: false,
    connectionSaveError: "",
  };
}

// Connections list

export function beginGameConnectionsLoad(state: MappingState): MappingState {
  return { ...state, isGameConnectionsLoading: true, gameConnectionsError: "" };
}

export function completeGameConnectionsLoad(state: MappingState, response: GameConnectionsResponse): MappingState {
  return { ...state, gameConnections: response.connections, isGameConnectionsLoading: false };
}

export function failGameConnectionsLoad(state: MappingState, error: string): MappingState {
  return { ...state, gameConnections: [], isGameConnectionsLoading: false, gameConnectionsError: error };
}

// Game searches (primary and secondary are independent)

function updateSlot(
  state: MappingState,
  slot: SearchSlot,
  change: (search: GameSearchState) => GameSearchState,
): MappingState {
  return { ...state, [slot]: change(state[slot]) };
}

export function setGameQuery(state: MappingState, slot: SearchSlot, query: string): MappingState {
  return updateSlot(state, slot, (search) => ({ ...search, query }));
}

export function beginGameSearch(state: MappingState, slot: SearchSlot): { state: MappingState; path: string | null } {
  const trimmed = state[slot].query.trim();
  if (trimmed.length === 0) {
    return { state: updateSlot(state, slot, (search) => ({ ...search, error: "Enter a search query." })), path: null };
  }
  return {
    state: updateSlot(state, slot, (search) => ({ ...search, isLoading: true, error: "" })),
    path: `/games/search?query=${encodeURIComponent(trimmed)}`,
  };
}

export function cancelGameSearch(state: MappingState, slot: SearchSlot): MappingState {
  return updateSlot(state, slot, (search) => ({ ...search, isLoading: false }));
}

export function completeGameSearch(state: MappingState, slot: SearchSlot, response: GameSearchResponse): MappingState {
  return updateSlot(state, slot, (search) => ({ ...search, results: response.results, isLoading: false }));
}

export function failGameSearch(state: MappingState, slot: SearchSlot, error: string): MappingState {
  return updateSlot(state, slot, (search) => ({ ...search, results: [], error, isLoading: false }));
}

export function selectGame(state: MappingState, slot: SearchSlot, game: GameSearchResult): MappingState {
  return updateSlot(state, slot, (search) => ({ ...search, selected: game, query: game.title, results: [], error: "" }));
}

// Connect / disconnect

export function buildConnectionSubmission(state: MappingState): { state: MappingState; submission: Submission | null } {
  const primary = state.primary.selected;
  const secondary = state.secondary.selected;
  if (primary === null || secondary === null) {
    return { state: { ...state, connectionSaveError: "Select two games to connect." }, submission: null };
  }
  if (primary.gameId === secondary.gameId) {
    return { state: { ...state, connectionSaveError: "Pick two different games to connect." }, submission: null };
  }
  return { state, submission: connectionSubmission(primary.gameId, secondary.gameId, "POST") };
}

export function buildDisconnectSubmission(gameId: string, connectedGameId: string): Submission {
  return connectionSubmission(gameId, connectedGameId, "DELETE");
}

export function beginConnectionSave(state: MappingState): MappingState {
  return { ...state, isConnectionSaving: true, connectionSaveError: "" };
}

export function completeConnect(state: MappingState): MappingState {
  const reset = (search: GameSearchState): GameSearchState => ({ ...search, selected: null, query: "", results: [] });
  return { ...state, primary: reset(state.primary), secondary: reset(state.secondary) };
}

export function failConnectionSave(state: MappingState, error: string): MappingState {
  return { ...state, connectionSaveError: error };
}

export function endConnectionSave(state: MappingState): MappingState {
  return { ...state, isConnectionSaving: false };
}

export function clearConnectionDraft(state: MappingState): MappingState {
  const reset = (search: GameSearchState): GameSearchState => ({
    ...search,
    selected: null,
    query: "",
    results: [],
    error: "",
  });
  return { ...state, primary: reset(state.primary), secondary: reset(state.secondary), connectionSaveError: "" };
}

function connectionSubmission(gameId: string, connectedGameId: string, method: "POST" | "DELETE"): Submission {
  return {
    path: gameConnectionsPath,
    method,
    body: { gameId: encodeGameId(gameId), connectedGameId: encodeGameId(connectedGameId) },
  };
}
