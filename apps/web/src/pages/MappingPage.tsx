import { Fragment, useEffect, useState } from "react";

import { apiRequest } from "../api";
import {
  buildGamePath,
  manualPlatformOptions,
  navItems as defaultNavItems,
} from "../constants";
import { encodeGameId } from "../utils/gameHash";
import type {
  GameConnectionChangeResponse,
  GameConnectionsResponse,
  GameSearchResponse,
  GameSearchResult,
  ManualInput,
  ManualInputsResponse,
  ManualMappingResponse,
  ManualMappingSearchResponse,
  ManualMappingSearchResult,
  NavItem,
} from "../types";
import { formatPlatformLabel } from "../utils/formatters";
import { AppHeader } from "../components/AppHeader";
import { Background } from "../components/Background";

type MappingPageProps = {
  path: string;
  navItems?: NavItem[];
  onNavigate: (path: string) => void;
  onLogout: () => void;
  theme: "light" | "dark";
  onToggleTheme: () => void;
};

export const MappingPage = ({
  path,
  navItems = defaultNavItems,
  onNavigate,
  onLogout,
  theme,
  onToggleTheme,
}: MappingPageProps) => {
  const [manualInputs, setManualInputs] = useState<ManualInput[]>([]);
  const [isManualInputsLoading, setIsManualInputsLoading] = useState(false);
  const [manualInputsError, setManualInputsError] = useState("");
  const [activeMappingInputId, setActiveMappingInputId] = useState<
    number | null
  >(null);
  const [mappingSearchQuery, setMappingSearchQuery] = useState("");
  const [mappingSearchResults, setMappingSearchResults] = useState<
    ManualMappingSearchResult[]
  >([]);
  const [mappingSearchError, setMappingSearchError] = useState("");
  const [isMappingSearchLoading, setIsMappingSearchLoading] = useState(false);
  const [mappingIgdbAvailable, setMappingIgdbAvailable] = useState(true);
  const [isMappingUpdateLoading, setIsMappingUpdateLoading] = useState(false);
  const [mappingPlatformOverride, setMappingPlatformOverride] =
    useState("auto");
  const [gameConnections, setGameConnections] = useState<
    GameConnectionsResponse["connections"]
  >([]);
  const [gameConnectionsError, setGameConnectionsError] = useState("");
  const [isGameConnectionsLoading, setIsGameConnectionsLoading] =
    useState(false);
  const [primaryGameQuery, setPrimaryGameQuery] = useState("");
  const [primaryGameResults, setPrimaryGameResults] = useState<
    GameSearchResult[]
  >([]);
  const [primaryGameError, setPrimaryGameError] = useState("");
  const [isPrimaryGameLoading, setIsPrimaryGameLoading] = useState(false);
  const [selectedPrimaryGame, setSelectedPrimaryGame] =
    useState<GameSearchResult | null>(null);
  const [secondaryGameQuery, setSecondaryGameQuery] = useState("");
  const [secondaryGameResults, setSecondaryGameResults] = useState<
    GameSearchResult[]
  >([]);
  const [secondaryGameError, setSecondaryGameError] = useState("");
  const [isSecondaryGameLoading, setIsSecondaryGameLoading] = useState(false);
  const [selectedSecondaryGame, setSelectedSecondaryGame] =
    useState<GameSearchResult | null>(null);
  const [isConnectionSaving, setIsConnectionSaving] = useState(false);
  const [connectionSaveError, setConnectionSaveError] = useState("");

  const loadManualInputs = async () => {
    setIsManualInputsLoading(true);
    setManualInputsError("");
    try {
      const response = await apiRequest<ManualInputsResponse>("/manual/inputs");
      setManualInputs(response.inputs);
      setIsManualInputsLoading(false);
    } catch (err) {
      setManualInputs([]);
      setIsManualInputsLoading(false);
      setManualInputsError(
        err instanceof Error ? err.message : "Failed to load manual inputs."
      );
    }
  };

  const handleMappingSearch = async () => {
    const query = mappingSearchQuery.trim();
    if (!query) {
      setMappingSearchError("Enter a search query.");
      return;
    }

    setIsMappingSearchLoading(true);
    setMappingSearchError("");
    try {
      const response = await apiRequest<ManualMappingSearchResponse>(
        `/manual/search?query=${encodeURIComponent(query)}`
      );
      setMappingIgdbAvailable(response.igdbAvailable);
      setMappingSearchResults(response.results);
    } catch (err) {
      setMappingSearchResults([]);
      setMappingSearchError(
        err instanceof Error ? err.message : "Failed to search IGDB."
      );
    } finally {
      setIsMappingSearchLoading(false);
    }
  };

  const loadGameConnections = async () => {
    setIsGameConnectionsLoading(true);
    setGameConnectionsError("");
    try {
      const response =
        await apiRequest<GameConnectionsResponse>("/games/connections");
      setGameConnections(response.connections);
    } catch (err) {
      setGameConnections([]);
      setGameConnectionsError(
        err instanceof Error ? err.message : "Failed to load game connections."
      );
    } finally {
      setIsGameConnectionsLoading(false);
    }
  };

  const searchGames = async (
    query: string,
    setResults: (results: GameSearchResult[]) => void,
    setError: (error: string) => void,
    setLoading: (loading: boolean) => void
  ) => {
    const trimmed = query.trim();
    if (!trimmed) {
      setError("Enter a search query.");
      return;
    }

    setLoading(true);
    setError("");
    try {
      const response = await apiRequest<GameSearchResponse>(
        `/games/search?query=${encodeURIComponent(trimmed)}`
      );
      setResults(response.results);
    } catch (err) {
      setResults([]);
      setError(err instanceof Error ? err.message : "Failed to search games.");
    } finally {
      setLoading(false);
    }
  };

  const handlePrimarySearch = () =>
    searchGames(
      primaryGameQuery,
      setPrimaryGameResults,
      setPrimaryGameError,
      setIsPrimaryGameLoading
    );

  const handleSecondarySearch = () =>
    searchGames(
      secondaryGameQuery,
      setSecondaryGameResults,
      setSecondaryGameError,
      setIsSecondaryGameLoading
    );

  const selectPrimaryGame = (game: GameSearchResult) => {
    setSelectedPrimaryGame(game);
    setPrimaryGameQuery(game.title);
    setPrimaryGameResults([]);
    setPrimaryGameError("");
  };

  const selectSecondaryGame = (game: GameSearchResult) => {
    setSelectedSecondaryGame(game);
    setSecondaryGameQuery(game.title);
    setSecondaryGameResults([]);
    setSecondaryGameError("");
  };

  const handleConnectGames = async () => {
    if (!selectedPrimaryGame || !selectedSecondaryGame) {
      setConnectionSaveError("Select two games to connect.");
      return;
    }
    if (selectedPrimaryGame.gameId === selectedSecondaryGame.gameId) {
      setConnectionSaveError("Pick two different games to connect.");
      return;
    }

    setIsConnectionSaving(true);
    setConnectionSaveError("");
    try {
      await apiRequest<GameConnectionChangeResponse>("/games/connections", {
        method: "POST",
        body: {
          gameId: encodeGameId(selectedPrimaryGame.gameId),
          connectedGameId: encodeGameId(selectedSecondaryGame.gameId),
        },
      });
      setSelectedPrimaryGame(null);
      setSelectedSecondaryGame(null);
      setPrimaryGameQuery("");
      setSecondaryGameQuery("");
      setPrimaryGameResults([]);
      setSecondaryGameResults([]);
      await loadGameConnections();
    } catch (err) {
      setConnectionSaveError(
        err instanceof Error ? err.message : "Failed to connect selected games."
      );
    } finally {
      setIsConnectionSaving(false);
    }
  };

  const handleDisconnectGames = async (
    gameId: string,
    connectedGameId: string
  ) => {
    setIsConnectionSaving(true);
    setConnectionSaveError("");
    try {
      await apiRequest<GameConnectionChangeResponse>("/games/connections", {
        method: "DELETE",
        body: {
          gameId: encodeGameId(gameId),
          connectedGameId: encodeGameId(connectedGameId),
        },
      });
      await loadGameConnections();
    } catch (err) {
      setConnectionSaveError(
        err instanceof Error ? err.message : "Failed to remove game connection."
      );
    } finally {
      setIsConnectionSaving(false);
    }
  };

  const handleMappingSelection = async (
    inputId: number,
    result: ManualMappingSearchResult
  ) => {
    setIsMappingUpdateLoading(true);
    setMappingSearchError("");
    try {
      if (result.source === "local" && result.gameId) {
        await apiRequest<ManualMappingResponse>(
          `/manual/inputs/${inputId}/map`,
          {
            method: "POST",
            body: { gameId: encodeGameId(result.gameId) },
          }
        );
      } else if (result.source === "igdb" && result.igdbId) {
        const platformKey =
          mappingPlatformOverride === "auto" ? null : mappingPlatformOverride;
        await apiRequest<ManualMappingResponse>(
          `/manual/inputs/${inputId}/map`,
          {
            method: "POST",
            body: {
              igdbId: result.igdbId,
              title: result.title,
              platformId: result.platformId ?? null,
              platformKey,
              capsuleUrl: result.capsuleUrl,
            },
          }
        );
      } else {
        setMappingSearchError("Invalid mapping selection.");
        return;
      }

      setActiveMappingInputId(null);
      setMappingSearchQuery("");
      setMappingSearchResults([]);
      setMappingPlatformOverride("auto");
      await loadManualInputs();
    } catch (err) {
      setMappingSearchError(
        err instanceof Error ? err.message : "Failed to map manual input."
      );
    } finally {
      setIsMappingUpdateLoading(false);
    }
  };

  const openMappingRow = (input: ManualInput) => {
    setActiveMappingInputId(input.id);
    setMappingSearchQuery(input.mappedGame?.title ?? input.input);
    setMappingSearchResults([]);
    setMappingSearchError("");
    setMappingPlatformOverride(
      input.mappedGame?.platform?.trim() ? input.mappedGame.platform : "auto"
    );
  };

  useEffect(() => {
    void loadManualInputs();
    void loadGameConnections();
  }, []);

  const handleGameSelect = (gameId: string) => {
    onNavigate(buildGamePath(gameId));
  };

  const renderMappingEditor = (input: ManualInput) => (
    <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4">
      <div className="flex flex-wrap items-end gap-3">
        <label className="flex-1 text-xs font-medium text-slate-600 dark:text-slate-300">
          Search IGDB
          <input
            type="text"
            value={mappingSearchQuery}
            onChange={(event) => setMappingSearchQuery(event.target.value)}
            className="mt-2 w-full rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-300 dark:focus:border-emerald-400 focus:ring-2 focus:ring-emerald-300/30 dark:focus:ring-emerald-400/30"
          />
        </label>
        <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
          Platform override
          <select
            value={mappingPlatformOverride}
            onChange={(event) => setMappingPlatformOverride(event.target.value)}
            className="mt-2 w-full min-w-[180px] rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-300 dark:focus:border-emerald-400 focus:ring-2 focus:ring-emerald-300/30 dark:focus:ring-emerald-400/30 disabled:cursor-not-allowed"
          >
            {manualPlatformOptions.map((platform) => (
              <option key={platform.key} value={platform.key}>
                {platform.label}
              </option>
            ))}
          </select>
        </label>
        <button
          type="button"
          onClick={handleMappingSearch}
          disabled={isMappingSearchLoading}
          className="rounded-full bg-emerald-300 dark:bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-200 dark:hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-emerald-200/60 dark:disabled:bg-emerald-300/60"
        >
          {isMappingSearchLoading ? "Searching..." : "Search"}
        </button>
        <button
          type="button"
          onClick={() => {
            setActiveMappingInputId(null);
            setMappingPlatformOverride("auto");
          }}
          className="rounded-full border border-slate-300 dark:border-slate-700 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-700 dark:text-slate-200"
        >
          Close
        </button>
      </div>

      {!mappingIgdbAvailable ? (
        <p className="mt-3 text-xs text-amber-700 dark:text-amber-200">
          IGDB is not configured on the server.
        </p>
      ) : null}
      {mappingSearchError ? (
        <p className="mt-3 text-xs text-rose-700 dark:text-rose-300">
          {mappingSearchError}
        </p>
      ) : null}

      <div className="mt-4 grid gap-3">
        {mappingSearchResults.length === 0 ? (
          <p className="text-xs text-slate-500 dark:text-slate-400">
            Search for a game to map this input.
          </p>
        ) : (
          mappingSearchResults.map((result) => (
            <div
              key={`${result.source}-${result.igdbId ?? result.gameId}-${result.title}`}
              className="grid grid-cols-[auto,1fr] items-start gap-x-3 gap-y-2 rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/40 dark:bg-slate-950/40 p-3"
            >
              <div className="pt-1">
                {result.capsuleUrl ? (
                  <img
                    src={result.capsuleUrl}
                    alt={`${result.title} cover`}
                    className="h-12 w-9 rounded-lg border border-slate-200 dark:border-slate-800 object-cover"
                  />
                ) : (
                  <div className="h-12 w-9 rounded-lg border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60" />
                )}
              </div>
              <div className="space-y-1">
                <button
                  type="button"
                  onClick={() =>
                    result.gameId ? handleGameSelect(result.gameId) : null
                  }
                  disabled={!result.gameId}
                  className="text-left text-sm font-semibold text-slate-900 dark:text-white transition hover:text-emerald-700 dark:hover:text-emerald-200"
                >
                  {result.title}
                </button>
                <p className="text-xs text-slate-500 dark:text-slate-400">
                  {result.source === "igdb"
                    ? mappingPlatformOverride === "auto"
                      ? (result.platform ?? "Unknown platform")
                      : formatPlatformLabel(mappingPlatformOverride)
                    : result.platform
                      ? formatPlatformLabel(result.platform)
                      : "Unknown platform"}{" "}
                  · {result.source.toUpperCase()}
                </p>
              </div>
              <div className="col-span-2">
                <button
                  type="button"
                  onClick={() => handleMappingSelection(input.id, result)}
                  disabled={isMappingUpdateLoading}
                  className="w-full rounded-full border border-emerald-300/60 dark:border-emerald-400/60 px-3 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-200 transition hover:border-emerald-300 dark:hover:border-emerald-400 disabled:cursor-not-allowed sm:w-auto"
                >
                  {isMappingUpdateLoading ? "Mapping..." : "Map"}
                </button>
              </div>
            </div>
          ))
        )}
      </div>
    </div>
  );

  return (
    <main className="relative min-h-screen overflow-hidden bg-slate-200 dark:bg-slate-950 text-slate-900 dark:text-slate-100">
      <Background />
      <div className="relative mx-auto w-full max-w-6xl p-3 sm:px-6 sm:py-12">
        <AppHeader
          title="Mapping"
          navItems={navItems}
          path={path}
          onNavigate={onNavigate}
          onLogout={onLogout}
          theme={theme}
          onToggleTheme={onToggleTheme}
        />

        <section className="mt-10 rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
          <div className="flex flex-wrap items-center justify-between gap-4">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                Manual mapping
              </p>
              <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
                Connect manual inputs to games
              </h2>
            </div>
            <button
              type="button"
              onClick={loadManualInputs}
              disabled={isManualInputsLoading}
              className="rounded-full border border-slate-300 dark:border-slate-700 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-700 dark:text-slate-200 transition hover:border-emerald-300/60 dark:hover:border-emerald-400/60 disabled:cursor-not-allowed"
            >
              {isManualInputsLoading ? "Refreshing..." : "Refresh"}
            </button>
          </div>

          {manualInputsError ? (
            <p className="mt-4 text-xs text-rose-700 dark:text-rose-300">
              {manualInputsError}
            </p>
          ) : null}

          <div className="mt-6 grid gap-4 md:hidden">
            {isManualInputsLoading ? (
              [1, 2, 3].map((row) => (
                <div
                  key={row}
                  className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/60 p-4"
                >
                  <div className="grid grid-cols-[auto,1fr] items-start gap-x-3 gap-y-3">
                    <div className="h-14 w-10 rounded-xl bg-slate-200/70 dark:bg-slate-800/60" />
                    <div className="flex-1 space-y-2">
                      <div className="h-3 w-32 rounded-full bg-slate-200/70 dark:bg-slate-800/60" />
                      <div className="h-3 w-40 rounded-full bg-slate-200/40 dark:bg-slate-800/40" />
                    </div>
                    <div className="col-span-2 h-9 rounded-full bg-slate-200/40 dark:bg-slate-800/40" />
                  </div>
                </div>
              ))
            ) : manualInputs.length === 0 ? (
              <div className="rounded-2xl border border-dashed border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4 text-sm text-slate-500 dark:text-slate-400">
                No manual inputs yet. Start a session to populate this list.
              </div>
            ) : (
              manualInputs.map((input) => (
                <div
                  key={input.id}
                  className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/60 p-4"
                >
                  <div className="grid grid-cols-[auto,1fr] items-start gap-x-3 gap-y-3">
                    <div className="pt-1">
                      {input.mappedGame?.capsuleUrl ? (
                        <img
                          src={input.mappedGame.capsuleUrl}
                          alt={`${input.mappedGame.title} cover`}
                          className="h-14 w-10 rounded-xl border border-slate-200 dark:border-slate-800 object-cover"
                        />
                      ) : (
                        <div className="h-14 w-10 rounded-xl border border-slate-200 dark:border-slate-800 bg-white/90 dark:bg-slate-900/70" />
                      )}
                    </div>
                    <div className="flex-1 space-y-2">
                      <p className="text-[11px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                        Input
                      </p>
                      <p className="text-sm font-semibold text-slate-900 dark:text-white">
                        {input.input}
                      </p>
                      <div className="text-xs text-slate-600 dark:text-slate-300">
                        {input.mappedGame ? (
                          <button
                            type="button"
                            onClick={() =>
                              input.mappedGame &&
                              handleGameSelect(input.mappedGame.id)
                            }
                            className="font-semibold text-slate-900 dark:text-white transition hover:text-emerald-700 dark:hover:text-emerald-200"
                          >
                            {input.mappedGame.title}
                            {input.mappedGame.platform
                              ? ` · ${formatPlatformLabel(
                                  input.mappedGame.platform
                                )}`
                              : ""}
                          </button>
                        ) : (
                          <span className="text-slate-500">Unmapped</span>
                        )}
                      </div>
                    </div>
                    <div className="col-span-2">
                      <button
                        type="button"
                        onClick={() => openMappingRow(input)}
                        className={`w-full rounded-full px-3 py-2 text-xs font-semibold uppercase tracking-[0.3em] transition sm:w-auto ${
                          input.mappedGame
                            ? "border border-slate-300 dark:border-slate-700 text-slate-700 dark:text-slate-200 hover:border-emerald-300/60 dark:hover:border-emerald-400/60 hover:text-emerald-700 dark:hover:text-emerald-100"
                            : "border border-emerald-300/60 dark:border-emerald-400/60 text-emerald-700 dark:text-emerald-200 hover:border-emerald-300 dark:hover:border-emerald-400"
                        }`}
                      >
                        {input.mappedGame ? "Edit mapping" : "Map"}
                      </button>
                    </div>
                  </div>

                  {activeMappingInputId === input.id ? (
                    <div className="mt-4">{renderMappingEditor(input)}</div>
                  ) : null}
                </div>
              ))
            )}
          </div>

          <div className="mt-6 hidden overflow-hidden rounded-2xl border border-slate-200 dark:border-slate-800 md:block">
            <table className="w-full text-left text-sm">
              <thead className="bg-white dark:bg-slate-950/70 text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                <tr>
                  <th className="px-4 py-3 text-left">Input</th>
                  <th className="px-4 py-3 text-left">Mapped game</th>
                  <th className="px-4 py-3 text-right">Action</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-200 dark:divide-slate-800 bg-white/80 dark:bg-slate-900/60">
                {isManualInputsLoading ? (
                  [1, 2, 3].map((row) => (
                    <tr key={row}>
                      <td className="px-4 py-4">
                        <div className="h-3 w-32 rounded-full bg-slate-200/70 dark:bg-slate-800/60" />
                      </td>
                      <td className="px-4 py-4">
                        <div className="h-3 w-40 rounded-full bg-slate-200/50 dark:bg-slate-800/50" />
                      </td>
                      <td className="px-4 py-4 text-right">
                        <div className="ml-auto h-8 w-20 rounded-full bg-slate-200/50 dark:bg-slate-800/50" />
                      </td>
                    </tr>
                  ))
                ) : manualInputs.length === 0 ? (
                  <tr>
                    <td
                      colSpan={3}
                      className="px-4 py-10 text-center text-sm text-slate-500 dark:text-slate-400"
                    >
                      No manual inputs yet. Start a session to populate this
                      list.
                    </td>
                  </tr>
                ) : (
                  manualInputs.map((input) => (
                    <Fragment key={input.id}>
                      <tr className="transition hover:bg-slate-100 dark:hover:bg-slate-900/70">
                        <td className="px-4 py-3 font-semibold text-slate-900 dark:text-white">
                          {input.input}
                        </td>
                        <td className="px-4 py-3 text-slate-600 dark:text-slate-300">
                          {input.mappedGame ? (
                            <span className="inline-flex items-center gap-2">
                              {input.mappedGame.capsuleUrl ? (
                                <img
                                  src={input.mappedGame.capsuleUrl}
                                  alt={`${input.mappedGame.title} cover`}
                                  className="h-10 w-8 rounded-lg border border-slate-200 dark:border-slate-800 object-cover"
                                />
                              ) : null}
                              <button
                                type="button"
                                onClick={() =>
                                  input.mappedGame &&
                                  handleGameSelect(input.mappedGame.id)
                                }
                                className="text-left text-sm font-semibold text-slate-900 dark:text-white transition hover:text-emerald-700 dark:hover:text-emerald-200"
                              >
                                {input.mappedGame.title}
                                {input.mappedGame.platform
                                  ? ` · ${formatPlatformLabel(
                                      input.mappedGame.platform
                                    )}`
                                  : ""}
                              </button>
                            </span>
                          ) : (
                            <span className="text-slate-500">Unmapped</span>
                          )}
                        </td>
                        <td className="px-4 py-3 text-right">
                          {input.mappedGame ? (
                            <button
                              type="button"
                              onClick={() => openMappingRow(input)}
                              className="rounded-full border border-slate-300 dark:border-slate-700 px-3 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-700 dark:text-slate-200 transition hover:border-emerald-300/60 dark:hover:border-emerald-400/60 hover:text-emerald-700 dark:hover:text-emerald-100"
                            >
                              Edit mapping
                            </button>
                          ) : (
                            <button
                              type="button"
                              onClick={() => openMappingRow(input)}
                              className="rounded-full border border-emerald-300/60 dark:border-emerald-400/60 px-3 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-200 transition hover:border-emerald-300 dark:hover:border-emerald-400"
                            >
                              Map
                            </button>
                          )}
                        </td>
                      </tr>
                      {activeMappingInputId === input.id ? (
                        <tr>
                          <td colSpan={3} className="px-4 py-4">
                            {renderMappingEditor(input)}
                          </td>
                        </tr>
                      ) : null}
                    </Fragment>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </section>

        <section className="mt-10 rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
          <div className="flex flex-wrap items-center justify-between gap-4">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                Game connections
              </p>
              <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
                Merge stats across editions
              </h2>
            </div>
            <button
              type="button"
              onClick={loadGameConnections}
              disabled={isGameConnectionsLoading}
              className="rounded-full border border-slate-300 dark:border-slate-700 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-700 dark:text-slate-200 transition hover:border-emerald-300/60 dark:hover:border-emerald-400/60 disabled:cursor-not-allowed"
            >
              {isGameConnectionsLoading ? "Refreshing..." : "Refresh"}
            </button>
          </div>

          {gameConnectionsError ? (
            <p className="mt-4 text-xs text-rose-700 dark:text-rose-300">
              {gameConnectionsError}
            </p>
          ) : null}

          <div className="mt-6 grid gap-4 lg:grid-cols-[1.1fr_0.9fr]">
            <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/50 p-4 sm:p-5">
              <p className="text-xs font-semibold uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                Connect games
              </p>
              <p className="mt-2 text-sm text-slate-600 dark:text-slate-300">
                Pick two games to combine their playtime into a single entry.
              </p>

              <div className="mt-4 grid gap-4">
                <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/60 p-4">
                  <p className="text-xs font-semibold uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                    Primary game
                  </p>
                  <div className="mt-3 flex flex-wrap items-center gap-3">
                    <input
                      type="text"
                      value={primaryGameQuery}
                      onChange={(event) =>
                        setPrimaryGameQuery(event.target.value)
                      }
                      className="flex-1 rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-300 dark:focus:border-emerald-400 focus:ring-2 focus:ring-emerald-300/30 dark:focus:ring-emerald-400/30"
                      placeholder="Search games"
                    />
                    <button
                      type="button"
                      onClick={handlePrimarySearch}
                      disabled={isPrimaryGameLoading}
                      className="rounded-full bg-emerald-300 dark:bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-200 dark:hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-emerald-200/60 dark:disabled:bg-emerald-300/60"
                    >
                      {isPrimaryGameLoading ? "Searching..." : "Search"}
                    </button>
                  </div>
                  {primaryGameError ? (
                    <p className="mt-3 text-xs text-rose-700 dark:text-rose-300">
                      {primaryGameError}
                    </p>
                  ) : null}
                  {selectedPrimaryGame ? (
                    <div className="mt-3 flex items-center gap-3 rounded-xl border border-emerald-300/40 dark:border-emerald-400/40 bg-emerald-300/10 dark:bg-emerald-500/10 p-3 text-sm text-emerald-700 dark:text-emerald-100">
                      {selectedPrimaryGame.capsuleUrl ? (
                        <img
                          src={selectedPrimaryGame.capsuleUrl}
                          alt={`${selectedPrimaryGame.title} cover`}
                          className="h-10 w-8 rounded-lg border border-emerald-300/40 dark:border-emerald-400/40 object-cover"
                        />
                      ) : (
                        <div className="h-10 w-8 rounded-lg border border-emerald-300/40 dark:border-emerald-400/40 bg-emerald-300/10 dark:bg-emerald-500/10" />
                      )}
                      <div>
                        <p className="font-semibold">
                          {selectedPrimaryGame.title}
                        </p>
                        <p className="text-xs text-emerald-700 dark:text-emerald-200">
                          {selectedPrimaryGame.platform
                            ? formatPlatformLabel(selectedPrimaryGame.platform)
                            : "Unknown platform"}
                        </p>
                      </div>
                    </div>
                  ) : null}
                  <div className="mt-3 grid gap-2">
                    {primaryGameResults.length === 0 ? (
                      <p className="text-xs text-slate-500">
                        Search to find the primary game.
                      </p>
                    ) : (
                      primaryGameResults.map((result) => (
                        <button
                          key={result.gameId}
                          type="button"
                          onClick={() => selectPrimaryGame(result)}
                          className="flex items-center gap-3 rounded-xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-950/40 p-3 text-left text-sm text-slate-900 dark:text-white transition hover:border-emerald-300/60 dark:hover:border-emerald-400/60"
                        >
                          {result.capsuleUrl ? (
                            <img
                              src={result.capsuleUrl}
                              alt={`${result.title} cover`}
                              className="h-10 w-8 rounded-lg border border-slate-200 dark:border-slate-800 object-cover"
                            />
                          ) : (
                            <div className="h-10 w-8 rounded-lg border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60" />
                          )}
                          <div>
                            <p className="font-semibold">{result.title}</p>
                            <p className="text-xs text-slate-500 dark:text-slate-400">
                              {result.platform
                                ? formatPlatformLabel(result.platform)
                                : "Unknown platform"}
                            </p>
                          </div>
                        </button>
                      ))
                    )}
                  </div>
                </div>

                <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/60 p-4">
                  <p className="text-xs font-semibold uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                    Connected game
                  </p>
                  <div className="mt-3 flex flex-wrap items-center gap-3">
                    <input
                      type="text"
                      value={secondaryGameQuery}
                      onChange={(event) =>
                        setSecondaryGameQuery(event.target.value)
                      }
                      className="flex-1 rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-300 dark:focus:border-emerald-400 focus:ring-2 focus:ring-emerald-300/30 dark:focus:ring-emerald-400/30"
                      placeholder="Search games"
                    />
                    <button
                      type="button"
                      onClick={handleSecondarySearch}
                      disabled={isSecondaryGameLoading}
                      className="rounded-full bg-emerald-300 dark:bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-200 dark:hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-emerald-200/60 dark:disabled:bg-emerald-300/60"
                    >
                      {isSecondaryGameLoading ? "Searching..." : "Search"}
                    </button>
                  </div>
                  {secondaryGameError ? (
                    <p className="mt-3 text-xs text-rose-700 dark:text-rose-300">
                      {secondaryGameError}
                    </p>
                  ) : null}
                  {selectedSecondaryGame ? (
                    <div className="mt-3 flex items-center gap-3 rounded-xl border border-emerald-300/40 dark:border-emerald-400/40 bg-emerald-300/10 dark:bg-emerald-500/10 p-3 text-sm text-emerald-700 dark:text-emerald-100">
                      {selectedSecondaryGame.capsuleUrl ? (
                        <img
                          src={selectedSecondaryGame.capsuleUrl}
                          alt={`${selectedSecondaryGame.title} cover`}
                          className="h-10 w-8 rounded-lg border border-emerald-300/40 dark:border-emerald-400/40 object-cover"
                        />
                      ) : (
                        <div className="h-10 w-8 rounded-lg border border-emerald-300/40 dark:border-emerald-400/40 bg-emerald-300/10 dark:bg-emerald-500/10" />
                      )}
                      <div>
                        <p className="font-semibold">
                          {selectedSecondaryGame.title}
                        </p>
                        <p className="text-xs text-emerald-700 dark:text-emerald-200">
                          {selectedSecondaryGame.platform
                            ? formatPlatformLabel(
                                selectedSecondaryGame.platform
                              )
                            : "Unknown platform"}
                        </p>
                      </div>
                    </div>
                  ) : null}
                  <div className="mt-3 grid gap-2">
                    {secondaryGameResults.length === 0 ? (
                      <p className="text-xs text-slate-500">
                        Search to find the connected game.
                      </p>
                    ) : (
                      secondaryGameResults.map((result) => (
                        <button
                          key={result.gameId}
                          type="button"
                          onClick={() => selectSecondaryGame(result)}
                          className="flex items-center gap-3 rounded-xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-950/40 p-3 text-left text-sm text-slate-900 dark:text-white transition hover:border-emerald-300/60 dark:hover:border-emerald-400/60"
                        >
                          {result.capsuleUrl ? (
                            <img
                              src={result.capsuleUrl}
                              alt={`${result.title} cover`}
                              className="h-10 w-8 rounded-lg border border-slate-200 dark:border-slate-800 object-cover"
                            />
                          ) : (
                            <div className="h-10 w-8 rounded-lg border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60" />
                          )}
                          <div>
                            <p className="font-semibold">{result.title}</p>
                            <p className="text-xs text-slate-500 dark:text-slate-400">
                              {result.platform
                                ? formatPlatformLabel(result.platform)
                                : "Unknown platform"}
                            </p>
                          </div>
                        </button>
                      ))
                    )}
                  </div>
                </div>
              </div>

              {connectionSaveError ? (
                <p className="mt-4 text-xs text-rose-700 dark:text-rose-300">
                  {connectionSaveError}
                </p>
              ) : null}

              <div className="mt-4 flex flex-wrap gap-3">
                <button
                  type="button"
                  onClick={handleConnectGames}
                  disabled={isConnectionSaving}
                  className="rounded-full bg-emerald-300 dark:bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-200 dark:hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-emerald-200/60 dark:disabled:bg-emerald-300/60"
                >
                  {isConnectionSaving ? "Saving..." : "Connect games"}
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setSelectedPrimaryGame(null);
                    setSelectedSecondaryGame(null);
                    setPrimaryGameQuery("");
                    setSecondaryGameQuery("");
                    setPrimaryGameResults([]);
                    setSecondaryGameResults([]);
                    setPrimaryGameError("");
                    setSecondaryGameError("");
                    setConnectionSaveError("");
                  }}
                  className="rounded-full border border-slate-300 dark:border-slate-700 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-700 dark:text-slate-200 transition hover:border-emerald-300/60 dark:hover:border-emerald-400/60"
                >
                  Clear
                </button>
              </div>
            </div>

            <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/50 p-4 sm:p-5">
              <p className="text-xs font-semibold uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                Connected pairs
              </p>
              <div className="mt-4 space-y-3">
                {isGameConnectionsLoading ? (
                  [1, 2, 3].map((row) => (
                    <div
                      key={`connection-${row}`}
                      className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/40 p-3"
                    >
                      <div className="flex items-center gap-3">
                        <div className="h-10 w-8 rounded-lg bg-slate-200/70 dark:bg-slate-800/60" />
                        <div className="h-3 w-24 rounded-full bg-slate-200/70 dark:bg-slate-800/60" />
                      </div>
                    </div>
                  ))
                ) : gameConnections.length === 0 ? (
                  <div className="rounded-2xl border border-dashed border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/40 p-4 text-xs text-slate-500 dark:text-slate-400">
                    No game connections yet.
                  </div>
                ) : (
                  gameConnections.map((connection) => (
                    <div
                      key={`${connection.gameId}-${connection.connectedGameId}`}
                      className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/40 p-3"
                    >
                      <div className="flex flex-col gap-3">
                        <div className="flex items-center gap-3">
                          {connection.capsuleUrl ? (
                            <img
                              src={connection.capsuleUrl}
                              alt={`${connection.title} cover`}
                              className="h-10 w-8 rounded-lg border border-slate-200 dark:border-slate-800 object-cover"
                            />
                          ) : (
                            <div className="h-10 w-8 rounded-lg border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60" />
                          )}
                          <div className="min-w-0 flex-1">
                            <button
                              type="button"
                              onClick={() =>
                                handleGameSelect(connection.gameId)
                              }
                              className="block w-full break-words text-left text-sm font-semibold text-slate-900 dark:text-white transition hover:text-emerald-700 dark:hover:text-emerald-200"
                            >
                              {connection.title}
                            </button>
                            <p className="text-xs text-slate-500 dark:text-slate-400">
                              {connection.platform
                                ? formatPlatformLabel(connection.platform)
                                : "Unknown platform"}
                            </p>
                          </div>
                        </div>
                        <div className="flex items-center gap-2 text-xs uppercase tracking-[0.3em] text-slate-500">
                          <span className="h-px flex-1 bg-slate-200 dark:bg-slate-800" />
                          <span>+ +</span>
                          <span className="h-px flex-1 bg-slate-200 dark:bg-slate-800" />
                        </div>
                        <div className="flex items-center gap-3">
                          {connection.connectedCapsuleUrl ? (
                            <img
                              src={connection.connectedCapsuleUrl}
                              alt={`${connection.connectedTitle} cover`}
                              className="h-10 w-8 rounded-lg border border-slate-200 dark:border-slate-800 object-cover"
                            />
                          ) : (
                            <div className="h-10 w-8 rounded-lg border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60" />
                          )}
                          <div className="min-w-0 flex-1">
                            <button
                              type="button"
                              onClick={() =>
                                handleGameSelect(connection.connectedGameId)
                              }
                              className="block w-full break-words text-left text-sm font-semibold text-slate-900 dark:text-white transition hover:text-emerald-700 dark:hover:text-emerald-200"
                            >
                              {connection.connectedTitle}
                            </button>
                            <p className="text-xs text-slate-500 dark:text-slate-400">
                              {connection.connectedPlatform
                                ? formatPlatformLabel(
                                    connection.connectedPlatform
                                  )
                                : "Unknown platform"}
                            </p>
                          </div>
                        </div>
                      </div>
                      <div className="mt-3 flex justify-end">
                        <button
                          type="button"
                          onClick={() =>
                            handleDisconnectGames(
                              connection.gameId,
                              connection.connectedGameId
                            )
                          }
                          disabled={isConnectionSaving}
                          className="rounded-full border border-rose-400/60 px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.3em] text-rose-700 dark:text-rose-200 transition hover:border-rose-300 hover:text-rose-700 dark:hover:text-rose-100 disabled:cursor-not-allowed"
                        >
                          Remove
                        </button>
                      </div>
                    </div>
                  ))
                )}
              </div>
            </div>
          </div>
        </section>
      </div>
    </main>
  );
};
