import { Fragment, useEffect, useState } from "react";

import { apiRequest } from "../api";
import { manualPlatformOptions, navItems as defaultNavItems } from "../constants";
import type {
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
};

export const MappingPage = ({
  path,
  navItems = defaultNavItems,
  onNavigate,
  onLogout,
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

  const loadManualInputs = async () => {
    setIsManualInputsLoading(true);
    setManualInputsError("");
    try {
      const response = await apiRequest<ManualInputsResponse>(
        "/manual/inputs"
      );
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

  const handleMappingSelection = async (
    inputId: number,
    result: ManualMappingSearchResult
  ) => {
    setIsMappingUpdateLoading(true);
    setMappingSearchError("");
    try {
      if (result.source === "local" && result.gameId) {
        await apiRequest<ManualMappingResponse>(`/manual/inputs/${inputId}/map`, {
          method: "POST",
          body: { gameId: result.gameId },
        });
      } else if (result.source === "igdb" && result.igdbId) {
        const platformKey =
          mappingPlatformOverride === "auto" ? null : mappingPlatformOverride;
        await apiRequest<ManualMappingResponse>(`/manual/inputs/${inputId}/map`, {
          method: "POST",
          body: {
            igdbId: result.igdbId,
            title: result.title,
            platformId: result.platformId ?? null,
            platformKey,
            capsuleUrl: result.capsuleUrl,
          },
        });
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
  }, []);

  return (
    <main className="relative min-h-screen overflow-hidden bg-slate-950 text-slate-100">
      <Background />
      <div className="relative mx-auto w-full max-w-6xl px-6 py-12">
        <AppHeader
          title="Mapping"
          navItems={navItems}
          path={path}
          onNavigate={onNavigate}
          onLogout={onLogout}
        />

        <section className="mt-10 rounded-3xl border border-slate-800 bg-slate-900/60 p-6">
          <div className="flex flex-wrap items-center justify-between gap-4">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
                Manual mapping
              </p>
              <h2 className="mt-2 text-xl font-semibold text-white">
                Connect manual inputs to games
              </h2>
            </div>
            <button
              type="button"
              onClick={loadManualInputs}
              disabled={isManualInputsLoading}
              className="rounded-full border border-slate-700 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-200 transition hover:border-emerald-400/60 disabled:cursor-not-allowed"
            >
              {isManualInputsLoading ? "Refreshing..." : "Refresh"}
            </button>
          </div>

          {manualInputsError ? (
            <p className="mt-4 text-xs text-rose-300">{manualInputsError}</p>
          ) : null}

          <div className="mt-6 overflow-hidden rounded-2xl border border-slate-800">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-950/70 text-xs uppercase tracking-[0.3em] text-slate-400">
                <tr>
                  <th className="px-4 py-3 text-left">Input</th>
                  <th className="px-4 py-3 text-left">Mapped game</th>
                  <th className="px-4 py-3 text-right">Action</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800 bg-slate-900/60">
                {isManualInputsLoading ? (
                  [1, 2, 3].map((row) => (
                    <tr key={row}>
                      <td className="px-4 py-4">
                        <div className="h-3 w-32 rounded-full bg-slate-800/60" />
                      </td>
                      <td className="px-4 py-4">
                        <div className="h-3 w-40 rounded-full bg-slate-800/50" />
                      </td>
                      <td className="px-4 py-4 text-right">
                        <div className="ml-auto h-8 w-20 rounded-full bg-slate-800/50" />
                      </td>
                    </tr>
                  ))
                ) : manualInputs.length === 0 ? (
                  <tr>
                    <td
                      colSpan={3}
                      className="px-4 py-10 text-center text-sm text-slate-400"
                    >
                      No manual inputs yet. Start a session to populate this
                      list.
                    </td>
                  </tr>
                ) : (
                  manualInputs.map((input) => (
                    <Fragment key={input.id}>
                      <tr className="transition hover:bg-slate-900/70">
                        <td className="px-4 py-3 font-semibold text-white">
                          {input.input}
                        </td>
                        <td className="px-4 py-3 text-slate-300">
                          {input.mappedGame ? (
                            <span className="inline-flex items-center gap-2">
                              {input.mappedGame.capsuleUrl ? (
                                <img
                                  src={input.mappedGame.capsuleUrl}
                                  alt={`${input.mappedGame.title} cover`}
                                  className="h-10 w-8 rounded-lg border border-slate-800 object-cover"
                                />
                              ) : null}
                              <span>
                                {input.mappedGame.title}
                                {input.mappedGame.platform
                                  ? ` · ${formatPlatformLabel(
                                      input.mappedGame.platform
                                    )}`
                                  : ""}
                              </span>
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
                              className="rounded-full border border-slate-700 px-3 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-200 transition hover:border-emerald-400/60 hover:text-emerald-100"
                            >
                              Edit mapping
                            </button>
                          ) : (
                            <button
                              type="button"
                              onClick={() => openMappingRow(input)}
                              className="rounded-full border border-emerald-400/60 px-3 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-emerald-200 transition hover:border-emerald-300"
                            >
                              Map
                            </button>
                          )}
                        </td>
                      </tr>
                      {activeMappingInputId === input.id ? (
                        <tr>
                          <td colSpan={3} className="px-4 py-4">
                            <div className="rounded-2xl border border-slate-800 bg-slate-950/50 p-4">
                              <div className="flex flex-wrap items-end gap-3">
                                <label className="flex-1 text-xs font-medium text-slate-300">
                                  Search IGDB
                                  <input
                                    type="text"
                                    value={mappingSearchQuery}
                                    onChange={(event) =>
                                      setMappingSearchQuery(event.target.value)
                                    }
                                    className="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950/80 px-3 py-2 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                                  />
                                </label>
                                <label className="text-xs font-medium text-slate-300">
                                  Platform override
                                  <select
                                    value={mappingPlatformOverride}
                                    onChange={(event) =>
                                      setMappingPlatformOverride(
                                        event.target.value
                                      )
                                    }
                                    className="mt-2 w-full min-w-[180px] rounded-xl border border-slate-700 bg-slate-950/80 px-3 py-2 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30 disabled:cursor-not-allowed"
                                  >
                                    {manualPlatformOptions.map((platform) => (
                                      <option
                                        key={platform.key}
                                        value={platform.key}
                                      >
                                        {platform.label}
                                      </option>
                                    ))}
                                  </select>
                                </label>
                                <button
                                  type="button"
                                  onClick={handleMappingSearch}
                                  disabled={isMappingSearchLoading}
                                  className="rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-emerald-300/60"
                                >
                                  {isMappingSearchLoading
                                    ? "Searching..."
                                    : "Search"}
                                </button>
                                <button
                                  type="button"
                                  onClick={() => {
                                    setActiveMappingInputId(null);
                                    setMappingPlatformOverride("auto");
                                  }}
                                  className="rounded-full border border-slate-700 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-200"
                                >
                                  Close
                                </button>
                              </div>

                              {!mappingIgdbAvailable ? (
                                <p className="mt-3 text-xs text-amber-200">
                                  IGDB is not configured on the server.
                                </p>
                              ) : null}
                              {mappingSearchError ? (
                                <p className="mt-3 text-xs text-rose-300">
                                  {mappingSearchError}
                                </p>
                              ) : null}

                              <div className="mt-4 grid gap-3">
                                {mappingSearchResults.length === 0 ? (
                                  <p className="text-xs text-slate-400">
                                    Search for a game to map this input.
                                  </p>
                                ) : (
                                  mappingSearchResults.map((result) => (
                                    <div
                                      key={`${result.source}-${result.igdbId ?? result.gameId}-${result.title}`}
                                      className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-slate-800 bg-slate-950/40 p-3"
                                    >
                                      <div className="flex items-center gap-3">
                                        {result.capsuleUrl ? (
                                          <img
                                            src={result.capsuleUrl}
                                            alt={`${result.title} cover`}
                                            className="h-12 w-9 rounded-lg border border-slate-800 object-cover"
                                          />
                                        ) : (
                                          <div className="h-12 w-9 rounded-lg border border-slate-800 bg-slate-900/60" />
                                        )}
                                        <div>
                                          <p className="text-sm font-semibold text-white">
                                            {result.title}
                                          </p>
                                          <p className="text-xs text-slate-400">
                                            {result.source === "igdb"
                                              ? mappingPlatformOverride ===
                                                "auto"
                                                ? result.platform ??
                                                  "Unknown platform"
                                                : formatPlatformLabel(
                                                    mappingPlatformOverride
                                                  )
                                              : result.platform
                                                ? formatPlatformLabel(
                                                    result.platform
                                                  )
                                                : "Unknown platform"}{" "}
                                            · {result.source.toUpperCase()}
                                          </p>
                                        </div>
                                      </div>
                                      <button
                                        type="button"
                                        onClick={() =>
                                          handleMappingSelection(input.id, result)
                                        }
                                        disabled={isMappingUpdateLoading}
                                        className="rounded-full border border-emerald-400/60 px-3 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-emerald-200 transition hover:border-emerald-300 disabled:cursor-not-allowed"
                                      >
                                        {isMappingUpdateLoading
                                          ? "Mapping..."
                                          : "Map"}
                                      </button>
                                    </div>
                                  ))
                                )}
                              </div>
                            </div>
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
      </div>
    </main>
  );
};
