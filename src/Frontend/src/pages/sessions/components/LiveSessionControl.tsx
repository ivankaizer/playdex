import { useEffect, useRef, useState } from "react";
import { isAbortError } from "@/lib/api";
import type {
  ManualActiveSession,
  ManualMappingSearchResponse,
  ManualMappingSearchResult,
  ManualPlatform,
  ManualSessionStartRequest,
} from "@/models/manual";
import { createLinkedController, escapeDataString } from "../support";
import { BoundSelect } from "./FormControls";

interface LiveSessionControlProps {
  platforms?: readonly ManualPlatform[];
  activeSession?: ManualActiveSession | null;
  busy?: boolean;
  error?: string;
  searchGames: (path: string, signal: AbortSignal) => Promise<ManualMappingSearchResponse>;
  onStart: (request: ManualSessionStartRequest) => void | Promise<void>;
  onStop: () => void | Promise<void>;
}

export function LiveSessionControl({
  platforms = [],
  activeSession = null,
  busy = false,
  error = "",
  searchGames,
  onStart,
  onStop,
}: LiveSessionControlProps) {
  const [query, setQuery] = useState("");
  const [platformKey, setPlatformKey] = useState("");
  const [results, setResults] = useState<readonly ManualMappingSearchResult[]>([]);
  const [selected, setSelected] = useState<ManualMappingSearchResult | null>(null);
  const [searching, setSearching] = useState(false);
  const searchingRef = useRef(false);
  const lifetime = useRef<AbortController | null>(null);
  const request = useRef<AbortController | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    lifetime.current = controller;
    return () => {
      controller.abort();
      request.current?.abort();
    };
  }, []);

  const changeQuery = (value: string) => {
    setQuery(value);
    setResults([]);
    setSelected(null);
  };

  const search = async () => {
    if (searchingRef.current || isNullOrWhiteSpace(query) || !lifetime.current) return;
    searchingRef.current = true;
    setSearching(true);
    request.current?.abort();
    const current = createLinkedController(lifetime.current.signal);
    request.current = current;
    try {
      const response = await searchGames(`/manual/search?query=${escapeDataString(query.trim())}`, current.signal);
      if (!current.signal.aborted) {
        setResults(response.results);
        setSelected(response.results.length === 1 ? response.results[0] : null);
      }
    } catch (exception) {
      // Blazor only swallowed cancellation; other failures surfaced as unhandled errors.
      if (!(isAbortError(exception) && current.signal.aborted)) throw exception;
    } finally {
      if (request.current === current) request.current = null;
      searchingRef.current = false;
      setSearching(false);
    }
  };

  const start = () => {
    if (selected === null) return;
    return onStart({
      title: selected.title,
      gameId: selected.gameId,
      platformKey,
      igdbId: selected.igdbId,
      capsuleUrl: selected.capsuleUrl,
    });
  };

  return (
    <section
      className="mt-5 rounded-2xl border border-slate-200 bg-white/70 p-4 dark:border-slate-800 dark:bg-slate-950/40"
      aria-label="Live session controls"
    >
      {activeSession !== null ? (
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div className="min-w-0">
            <p className="text-[10px] font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">Live manual session</p>
            <p className="mt-1 truncate text-sm font-semibold text-slate-900 dark:text-white">
              {activeSession.title} · {activeSession.platformName}
            </p>
          </div>
          <button
            type="button"
            disabled={busy}
            onClick={() => void onStop()}
            className="rounded-full border border-rose-500/60 px-4 py-2 text-xs font-semibold uppercase tracking-[0.2em] text-rose-700 transition hover:border-rose-400 disabled:opacity-60 dark:text-rose-200"
          >
            {busy ? "Stopping..." : "Stop session"}
          </button>
        </div>
      ) : (
        <>
          <div className="flex flex-wrap items-end gap-3">
            <label className="min-w-[14rem] flex-1 text-xs font-medium text-slate-600 dark:text-slate-300">
              Start a live session
              <input
                type="search"
                value={query}
                onChange={(event) => changeQuery(event.currentTarget.value)}
                placeholder="Search saved games and IGDB"
                autoComplete="off"
                className="mt-2 w-full rounded-xl border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 outline-none focus:border-emerald-400 dark:border-slate-700 dark:bg-slate-950 dark:text-white"
              />
            </label>
            <button
              type="button"
              disabled={searching || isNullOrWhiteSpace(query)}
              onClick={() => void search()}
              className="rounded-full border border-slate-300 px-4 py-2 text-xs font-semibold uppercase tracking-[0.2em] text-slate-700 disabled:opacity-60 dark:border-slate-700 dark:text-slate-200"
            >
              {searching ? "Searching..." : "Search"}
            </button>
            <label data-live-platform className="w-full text-xs font-medium text-slate-600 dark:text-slate-300 sm:w-36 sm:min-w-0">
              Platform
              <BoundSelect
                value={platformKey}
                onValueChange={setPlatformKey}
                className="mt-2 w-full rounded-xl border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 dark:border-slate-700 dark:bg-slate-950 dark:text-white"
              >
                <option value="">Select</option>
                {platforms.map((platform, index) => (
                  <option key={`${index}-${platform.key}`} value={platform.key}>
                    {platform.name}
                  </option>
                ))}
              </BoundSelect>
            </label>
            <button
              type="button"
              disabled={searching || selected === null || isNullOrWhiteSpace(platformKey)}
              onClick={() => void start()}
              className="rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.2em] text-slate-950 disabled:opacity-60"
            >
              Start live session
            </button>
          </div>
          {results.length > 0 && (
            <div className="mt-3 grid gap-2 sm:grid-cols-2">
              {results.map((result, index) => (
                <button
                  type="button"
                  key={result.gameId ?? result.igdbId ?? `result-${index}`}
                  onClick={() => setSelected(result)}
                  className={`flex items-center gap-3 rounded-xl border p-2 text-left ${selected === result ? "border-emerald-400 bg-emerald-50 dark:bg-emerald-400/10" : "border-slate-200 dark:border-slate-700"}`}
                >
                  {result.capsuleUrl !== null ? (
                    <img src={result.capsuleUrl} alt={`${result.title} poster`} className="h-12 w-9 rounded object-cover" />
                  ) : (
                    <span
                      aria-hidden="true"
                      className="flex h-12 w-9 items-center justify-center rounded bg-slate-200 text-[8px] uppercase text-slate-500 dark:bg-slate-800 dark:text-slate-400"
                    >
                      No art
                    </span>
                  )}
                  <span className="min-w-0">
                    <span className="block truncate text-sm font-semibold text-slate-900 dark:text-white">{result.title}</span>
                    <span className="text-xs text-slate-500 dark:text-slate-400">{result.source === "local" ? "Saved game" : "IGDB"}</span>
                  </span>
                </button>
              ))}
            </div>
          )}
        </>
      )}
      {!isNullOrWhiteSpace(error) && (
        <p role="alert" className="mt-3 text-sm text-rose-700 dark:text-rose-300">
          {error}
        </p>
      )}
    </section>
  );
}

function isNullOrWhiteSpace(value: string | null | undefined): boolean {
  return value == null || value.trim().length === 0;
}
