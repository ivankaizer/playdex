import { useCallback, useEffect, useRef } from "react";
import { apiRequest, errorMessage, isAbortError } from "@/lib/api";
import { encodeGameId } from "@/lib/gameHash";
import type { GameConnectionChangeResponse, GameConnectionsResponse, GameSearchResponse, GameSearchResult } from "@/models/games";
import type { Submission } from "../models";
import { LatestRequest } from "../shared/latestRequest";
import { isLifetimeCancellation } from "../shared/lifetime";
import { useStore } from "../shared/useStore";
import * as mapping from "./mappingState";
import type { SearchSlot } from "./mappingState";

// Blazor begins the initial load before the first render, so the first paint already shows loading.
const initialMappingState = () => mapping.beginGameConnectionsLoad(mapping.createMappingState());

/** Game connections ("merge stats across editions") embedded in Settings. */
export function useMapping() {
  const [state, update, get] = useStore(initialMappingState);
  const lifetime = useRef(new AbortController());
  const connectionsRequest = useRef(new LatestRequest());
  const searchRequests = useRef<Record<SearchSlot, LatestRequest>>({
    primary: new LatestRequest(),
    secondary: new LatestRequest(),
  });

  const loadGameConnections = useCallback(async () => {
    update(mapping.beginGameConnectionsLoad);
    const request = connectionsRequest.current.begin();
    try {
      const response = await apiRequest<GameConnectionsResponse>(mapping.gameConnectionsPath, {
        signal: request.signal,
      });
      if (request.isCurrent() && !request.signal.aborted) {
        update((current) => mapping.completeGameConnectionsLoad(current, response));
      }
    } catch (error) {
      if (isAbortError(error) && request.signal.aborted) return;
      if (request.isCurrent()) update((current) => mapping.failGameConnectionsLoad(current, errorMessage(error)));
    } finally {
      request.finish();
    }
  }, [update]);

  useEffect(() => {
    const controller = new AbortController();
    lifetime.current = controller;
    const connections = connectionsRequest.current;
    const searches = searchRequests.current;
    void loadGameConnections();
    return () => {
      controller.abort();
      connections.invalidate();
      searches.primary.invalidate();
      searches.secondary.invalidate();
    };
  }, [loadGameConnections]);

  const changeGameQuery = useCallback(
    (slot: SearchSlot, query: string) => {
      update((current) => mapping.setGameQuery(current, slot, query));
      searchRequests.current[slot].invalidate();
      update((current) => mapping.cancelGameSearch(current, slot));
    },
    [update],
  );

  const searchGames = useCallback(
    async (slot: SearchSlot) => {
      const { state: begun, path } = mapping.beginGameSearch(get(), slot);
      update(() => begun);
      if (path === null) return;

      const request = searchRequests.current[slot].begin();
      try {
        const response = await apiRequest<GameSearchResponse>(path, { signal: request.signal });
        if (!request.isCurrent() || request.signal.aborted) return;
        update((current) => mapping.completeGameSearch(current, slot, response));
      } catch (error) {
        if (isAbortError(error) && request.signal.aborted) return;
        if (request.isCurrent()) update((current) => mapping.failGameSearch(current, slot, errorMessage(error)));
      } finally {
        request.finish();
      }
    },
    [get, update],
  );

  const selectGame = useCallback(
    (slot: SearchSlot, game: GameSearchResult) => update((current) => mapping.selectGame(current, slot, game)),
    [update],
  );

  const sendConnectionChange = useCallback(
    async (submission: Submission, onSuccess?: () => void) => {
      update(mapping.beginConnectionSave);
      const signal = lifetime.current.signal;
      try {
        await apiRequest<GameConnectionChangeResponse>(submission.path, {
          method: submission.method,
          body: submission.body,
          signal,
        });
        onSuccess?.();
        await loadGameConnections();
      } catch (error) {
        if (!isLifetimeCancellation(error, signal)) {
          update((current) => mapping.failConnectionSave(current, errorMessage(error)));
        }
      } finally {
        update(mapping.endConnectionSave);
      }
    },
    [loadGameConnections, update],
  );

  const connectGames = useCallback(async () => {
    const { state: validated, submission } = mapping.buildConnectionSubmission(get());
    update(() => validated);
    if (!submission) return;
    await sendConnectionChange(submission, () => update(mapping.completeConnect));
  }, [get, sendConnectionChange, update]);

  const disconnectGames = useCallback(
    (gameId: string, connectedGameId: string) => sendConnectionChange(mapping.buildDisconnectSubmission(gameId, connectedGameId)),
    [sendConnectionChange],
  );

  const clearConnectionDraft = useCallback(() => update(mapping.clearConnectionDraft), [update]);

  const gamePath = useCallback((gameId: string) => `/games/${encodeGameId(gameId)}`, []);

  return {
    state,
    loadGameConnections,
    changeGameQuery,
    searchGames,
    selectGame,
    connectGames,
    disconnectGames,
    clearConnectionDraft,
    gamePath,
  };
}
