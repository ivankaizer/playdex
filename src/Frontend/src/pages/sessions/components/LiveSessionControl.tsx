import { useEffect, useRef, useState } from "react";
import { AppLink } from "@/components/AppLink";
import { Cover } from "@/components/ui";
import { isAbortError } from "@/lib/api";
import { cx } from "@/lib/cx";
import { formatCount } from "@/lib/format";
import type {
  ManualActiveSession,
  ManualMappingSearchResponse,
  ManualMappingSearchResult,
  ManualPlatform,
  ManualSessionStartRequest,
} from "@/models/manual";
import { createLinkedController, escapeDataString } from "../support";

interface LiveSessionControlProps {
  platforms?: readonly ManualPlatform[];
  activeSession?: ManualActiveSession | null;
  busy?: boolean;
  error?: string;
  /** False for a synced (non-manual) session that can only be watched, not stopped. Defaults to true. */
  canStop?: boolean;
  /** When set, the live game title links to the game's page at this path. */
  gamePath?: (gameId: string) => string;
  searchGames: (path: string, signal: AbortSignal) => Promise<ManualMappingSearchResponse>;
  onStart: (request: ManualSessionStartRequest) => void | Promise<void>;
  onStop: () => void | Promise<void>;
}

/** Slim "current session" bar: idle (search, platform, start) or live (cover, timer, stop). */
export function LiveSessionControl({
  platforms = [],
  activeSession = null,
  busy = false,
  error = "",
  canStop = true,
  gamePath,
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

  const live = activeSession !== null;
  const expanded = !live && results.length > 0;

  return (
    <section
      aria-label="Live session controls"
      className={cx(
        "pdx-card !py-3 !pl-7 !pr-3 max-sm:!pl-5",
        // A full pill only while the bar fits on one row; once it wraps (≤1040px) it becomes a 28px card.
        expanded ? "!rounded-[28px]" : "!rounded-[28px] min-[1041px]:!rounded-full",
      )}
      style={live ? { background: "var(--live-bg)" } : undefined}
    >
      {live ? (
        <LiveRow session={activeSession} busy={busy} canStop={canStop} gamePath={gamePath} onStop={onStop} />
      ) : (
        <>
          <div className="flex flex-wrap items-center gap-2.5">
            <div className="mr-1.5 flex items-center gap-2 text-[11px] font-semibold uppercase tracking-[0.24em] text-accent">
              <span aria-hidden="true" className="h-[7px] w-[7px] rounded-full bg-faint-2"></span>
              No session
            </div>
            <form
              role="search"
              className="flex min-w-0 flex-[1_1_220px] items-center gap-2"
              onSubmit={(event) => {
                event.preventDefault();
                void search();
              }}
            >
              <input
                type="search"
                value={query}
                onChange={(event) => changeQuery(event.currentTarget.value)}
                placeholder="Search saved games and IGDB"
                aria-label="Start a live session"
                autoComplete="off"
                className="pdx-input"
              />
              <button
                type="submit"
                disabled={searching || isNullOrWhiteSpace(query)}
                aria-label={searching ? "Searching..." : "Search"}
                title="Search"
                className="pdx-icon-btn !h-10 !w-10 disabled:cursor-not-allowed disabled:opacity-60"
              >
                <svg
                  aria-hidden="true"
                  viewBox="0 0 24 24"
                  className="h-4 w-4"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                >
                  <circle cx="11" cy="11" r="7"></circle>
                  <path d="m20 20-3.5-3.5"></path>
                </svg>
              </button>
            </form>
            <div className="flex flex-wrap items-center gap-3">
              {platforms.length > 0 && (
                <div data-live-platform role="group" aria-label="Platform" className="pdx-seg">
                  {platforms.map((platform, index) => (
                    <button
                      key={`${index}-${platform.key}`}
                      type="button"
                      aria-pressed={platform.key === platformKey ? "true" : "false"}
                      onClick={() => setPlatformKey(platform.key)}
                      className="pdx-seg-btn !px-3 !font-medium"
                    >
                      {platform.name}
                    </button>
                  ))}
                </div>
              )}
              <button
                type="button"
                disabled={searching || selected === null || isNullOrWhiteSpace(platformKey)}
                onClick={() => void start()}
                className="pdx-btn-primary !px-6"
              >
                <svg aria-hidden="true" viewBox="0 0 24 24" className="h-3.5 w-3.5" fill="currentColor">
                  <path d="M7 4.5v15a1 1 0 0 0 1.52.85l12-7.5a1 1 0 0 0 0-1.7l-12-7.5A1 1 0 0 0 7 4.5Z"></path>
                </svg>
                Start session
              </button>
            </div>
          </div>
          {results.length > 0 && (
            <div className="mt-3 grid gap-2 pb-2 pr-2 [grid-template-columns:repeat(auto-fill,minmax(min(260px,100%),1fr))]">
              {results.map((result, index) => (
                <button
                  type="button"
                  key={result.gameId ?? result.igdbId ?? `result-${index}`}
                  aria-pressed={selected === result ? "true" : "false"}
                  onClick={() => setSelected(result)}
                  className={cx(
                    "flex items-center gap-3 rounded-[18px] border p-2 text-left transition-colors",
                    selected === result ? "border-mint-line bg-mint" : "border-line bg-surface hover:border-mint-hover",
                  )}
                >
                  <Cover title={result.title} src={result.capsuleUrl} className="h-10 w-10 rounded-[12px]" initialClassName="text-sm" />
                  <span className="min-w-0">
                    <span className="block truncate text-sm font-semibold text-ink">{result.title}</span>
                    <span className="text-xs text-muted">{result.source === "local" ? "Saved game" : "IGDB"}</span>
                  </span>
                </button>
              ))}
            </div>
          )}
        </>
      )}
      {!isNullOrWhiteSpace(error) && (
        <p role="alert" className="mt-2 px-1 pb-1 text-sm text-danger">
          {error}
        </p>
      )}
    </section>
  );
}

function LiveRow({
  session,
  busy,
  canStop,
  gamePath,
  onStop,
}: {
  session: ManualActiveSession;
  busy: boolean;
  canStop: boolean;
  gamePath?: (gameId: string) => string;
  onStop: () => void | Promise<void>;
}) {
  const now = useNow();
  const { timer, since, staleFor } = liveTimerView(session.startedAt, now);

  return (
    <div className="flex flex-wrap items-center gap-4">
      <Cover title={session.title} src={session.capsuleUrl} className="h-10 w-10 rounded-full" initialClassName="text-[15px]" />
      <div className="min-w-0 flex-[1_1_220px]">
        <div className="flex items-center gap-2 text-[10px] font-semibold uppercase tracking-[0.24em] text-accent">
          <span aria-hidden="true" className="pdx-live-dot"></span>
          Playing now
        </div>
        <p className="mt-px truncate text-[15px] font-semibold" title={session.title}>
          {gamePath ? (
            <AppLink
              to={gamePath(session.gameId)}
              className="inline-block max-w-full truncate font-semibold no-underline hover:text-accent"
            >
              {session.title}
            </AppLink>
          ) : (
            session.title
          )}
        </p>
        <p className="truncate text-[13px] text-muted">
          {session.platformName}
          {since && ` · since ${since}`}
        </p>
        {staleFor && <p className="mt-0.5 text-xs font-semibold text-warn">Still playing? This session has been running for {staleFor}.</p>}
      </div>
      <span className="pdx-num text-2xl font-semibold tracking-[-0.01em] text-accent" aria-label={`Elapsed ${timer}`} role="timer">
        {timer}
      </span>
      {canStop && (
        <button type="button" disabled={busy} onClick={() => void onStop()} className="pdx-btn-danger !px-[22px]">
          <span aria-hidden="true" className="inline-block h-2.5 w-2.5 rounded-[2px] bg-current"></span>
          {busy ? "Stopping..." : "Stop and log"}
        </button>
      )}
    </div>
  );
}

const weekdayFormat = new Intl.DateTimeFormat("en-US", { weekday: "short" });

/**
 * Live timer texts: "H:MM:SS" under a day, "Nd Nh Nm" past it (with the start weekday in `since`), and
 * a "Still playing?" duration once the session has run 12 hours or more.
 */
export function liveTimerView(startedAt: string, now: number): { timer: string; since: string; staleFor: string } {
  const started = new Date(startedAt);
  const valid = !Number.isNaN(started.getTime());
  const elapsed = valid ? Math.max(0, Math.floor((now - started.getTime()) / 1000)) : 0;
  const days = Math.floor(elapsed / 86400);
  const timer = days
    ? `${days}d ${Math.floor(elapsed / 3600) % 24}h ${Math.floor(elapsed / 60) % 60}m`
    : `${Math.floor(elapsed / 3600)}:${pad2(Math.floor(elapsed / 60) % 60)}:${pad2(elapsed % 60)}`;
  const clock = `${pad2(started.getHours())}:${pad2(started.getMinutes())}`;
  const since = !valid ? "" : days ? `${weekdayFormat.format(started)} ${clock}` : clock;
  const staleFor = elapsed >= 12 * 3600 ? `over ${formatCount(Math.floor(elapsed / 3600))} hours` : "";
  return { timer, since, staleFor };
}

function useNow(): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, []);
  return now;
}

function pad2(value: number): string {
  return String(value).padStart(2, "0");
}

function isNullOrWhiteSpace(value: string | null | undefined): boolean {
  return value == null || value.trim().length === 0;
}
