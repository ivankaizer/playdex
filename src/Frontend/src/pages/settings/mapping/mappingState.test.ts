import { describe, expect, it } from "vitest";
import type { GameSearchResult } from "@/models/games";
import * as m from "./mappingState";

function game(gameId: string, title: string): GameSearchResult {
  return { gameId, title, platform: "steam", capsuleUrl: null };
}

describe("mappingState", () => {
  it("keeps game searches independent and selecting does not bind later query edits", () => {
    const primary = game("42", "Hades II");
    const secondary = game("99", "Celeste");
    let state = m.completeGameSearch(m.createMappingState(), "primary", { query: "old", results: [primary] });
    state = m.completeGameSearch(state, "secondary", { query: "old", results: [secondary] });

    state = m.setGameQuery(state, "primary", " A! (Test)*~' ");
    const begun = m.beginGameSearch(state, "primary");
    expect(begun.path).toBe("/games/search?query=A!%20(Test)*~'");
    state = begun.state;
    expect(state.primary.isLoading).toBe(true);
    expect(state.primary.results).toHaveLength(1);
    expect(state.secondary.isLoading).toBe(false);
    expect(state.secondary.results).toHaveLength(1);

    state = m.failGameSearch(state, "primary", "Primary failed.");
    expect(state.primary.results).toEqual([]);
    expect(state.primary.error).toBe("Primary failed.");
    expect(state.secondary.results).toHaveLength(1);

    state = m.selectGame(state, "secondary", secondary);
    expect(state.secondary.selected).toBe(secondary);
    expect(state.secondary.query).toBe("Celeste");
    expect(state.secondary.results).toEqual([]);
    expect(state.secondary.error).toBe("");

    state = m.setGameQuery(state, "secondary", "edited title");
    expect(state.secondary.selected).toBe(secondary);
    expect(state.secondary.query).toBe("edited title");
  });

  it("requires a search query", () => {
    const begun = m.beginGameSearch(m.setGameQuery(m.createMappingState(), "secondary", "   "), "secondary");
    expect(begun.path).toBeNull();
    expect(begun.state.secondary.error).toBe("Enter a search query.");
    expect(begun.state.secondary.isLoading).toBe(false);
  });

  it("validates connections, builds bodies, completes and clears", () => {
    let state = m.createMappingState();
    let result = m.buildConnectionSubmission(state);
    expect(result.submission).toBeNull();
    expect(result.state.connectionSaveError).toBe("Select two games to connect.");

    const primary = game("42", "Hades II");
    state = m.selectGame(m.selectGame(state, "primary", primary), "secondary", primary);
    result = m.buildConnectionSubmission(state);
    expect(result.submission).toBeNull();
    expect(result.state.connectionSaveError).toBe("Pick two different games to connect.");

    state = m.selectGame(state, "secondary", game("99", "Celeste"));
    const connect = m.buildConnectionSubmission(state).submission!;
    expect(connect).toEqual({ path: "/games/connections", method: "POST", body: { gameId: "3H4h3", connectedGameId: "8HWvi" } });
    expect(Object.keys(connect.body)).toEqual(["gameId", "connectedGameId"]);

    const disconnect = m.buildDisconnectSubmission("42", "99");
    expect(disconnect).toEqual({ ...connect, method: "DELETE" });

    state = m.setGameQuery(m.setGameQuery(state, "primary", "primary draft"), "secondary", "secondary draft");
    state = m.failGameSearch(m.failGameSearch(state, "primary", "primary error"), "secondary", "secondary error");
    state = m.beginConnectionSave(state);
    expect(state.isConnectionSaving).toBe(true);
    expect(state.connectionSaveError).toBe("");
    state = m.completeConnect(state);
    expect(state.isConnectionSaving).toBe(true);
    state = m.endConnectionSave(state);
    expect(state.isConnectionSaving).toBe(false);
    expect(state.primary).toMatchObject({ selected: null, query: "", results: [], error: "primary error" });
    expect(state.secondary).toMatchObject({ selected: null, query: "", results: [], error: "secondary error" });

    state = m.selectGame(m.selectGame(state, "primary", primary), "secondary", game("99", "Celeste"));
    state = m.failGameSearch(m.failGameSearch(state, "primary", "again"), "secondary", "again");
    state = m.clearConnectionDraft(m.failConnectionSave(state, "save error"));
    expect(state.primary.selected).toBeNull();
    expect(state.secondary.selected).toBeNull();
    expect(state.primary.error).toBe("");
    expect(state.secondary.error).toBe("");
    expect(state.connectionSaveError).toBe("");
  });

  it("loads connections, keeping old ones while loading and clearing them on failure", () => {
    let state = m.beginGameConnectionsLoad(m.createMappingState());
    expect(state.isGameConnectionsLoading).toBe(true);
    state = m.failGameConnectionsLoad(state, "Nope.");
    expect(state).toMatchObject({ isGameConnectionsLoading: false, gameConnectionsError: "Nope.", gameConnections: [] });
    state = m.beginGameConnectionsLoad(state);
    expect(state.gameConnectionsError).toBe("");
  });
});
