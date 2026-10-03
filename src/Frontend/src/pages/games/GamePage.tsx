import { useEffect, useRef, useState, type ChangeEvent } from "react";
import { AppLayout } from "@/components/AppLayout";
import { apiRequest, errorMessage } from "@/lib/api";
import { getDurationSeconds } from "@/lib/dates";
import { formatDateLabel, formatDuration, formatHours, formatTimeLabel } from "@/lib/format";
import { logout } from "@/lib/navigation";
import type { GameIgnoreResponse, GameRatingResponse, GameResponse, GameStatsResponse } from "@/models/games";
import type { AccountSessionsResponse } from "@/models/sessions";
import {
  activeYear,
  availableYears,
  beginFirstSessionsLoad,
  beginGame,
  beginIgnoreSave,
  beginRatingSave,
  beginRecentPageLoad,
  beginStatsLoad,
  buildIgnoreSubmission,
  buildRatingSubmission,
  completeDetails,
  completeFirstSessions,
  completeIgnore,
  completeRating,
  completeSessionsPage,
  completeStats,
  displayTitle,
  endIgnoreSave,
  endRatingSave,
  failDetails,
  failIgnore,
  failRating,
  failSessions,
  failStats,
  gameImage,
  isRatingDisabled,
  lastPlayedLabel,
  monthlyStats,
  ratingLabel,
  recentTotalPages,
  sessionsPagePath,
  setSelectedYear,
  totalSeconds,
  yearlyStats,
  type GameChartBucket,
  type GameRequests,
  type GameState,
} from "./gameState";

const pagerButtonClass =
  "rounded-full border border-slate-200 dark:border-slate-800 px-4 py-2 text-slate-700 dark:text-slate-200 transition hover:border-emerald-400/60 hover:text-emerald-700 dark:hover:text-emerald-200 disabled:cursor-not-allowed disabled:border-slate-200 dark:disabled:border-slate-800 disabled:text-slate-600";
const tooltipClass =
  "pointer-events-none absolute left-1/2 top-[-6px] z-10 -translate-x-1/2 -translate-y-full rounded-md bg-white/90 dark:bg-slate-900/90 px-3 py-1 text-[10px] font-semibold text-slate-900 dark:text-slate-100 opacity-0 shadow-lg ring-1 ring-slate-300/60 dark:ring-slate-700/60 transition duration-150 group-hover:opacity-100";

/** The game detail page. `hash` is the normalized game hash (null for an invalid link). */
export function GamePage({ hash }: { hash: string | null }) {
  const [state, setState] = useState<GameState>(() => beginGame(hash, new Date()));
  const pageRequest = useRef<AbortController | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    pageRequest.current = controller;
    const { signal } = controller;
    const isCurrent = () => !signal.aborted;
    const update = (transition: (current: GameState) => GameState) => {
      if (isCurrent()) setState(transition);
    };

    const initial = beginGame(hash, new Date());
    setState(initial);
    const requests = initial.requests;
    if (requests === null) {
      return () => controller.abort();
    }

    const loadDetails = async () => {
      try {
        const response = await apiRequest<GameResponse>(requests.details, { onAuthFailure: logout, signal });
        update((current) => completeDetails(current, response));
      } catch (error) {
        update((current) => failDetails(current, errorMessage(error)));
      }
    };

    const loadStats = async () => {
      setState(beginStatsLoad);
      try {
        const response = await apiRequest<GameStatsResponse>(requests.stats, { onAuthFailure: logout, signal });
        update((current) => completeStats(current, response));
      } catch (error) {
        update((current) => failStats(current, errorMessage(error)));
      }
    };

    const loadIgnore = async (target: GameRequests) => {
      try {
        const response = await apiRequest<GameIgnoreResponse>(target.ignoreStatus, { onAuthFailure: logout, signal });
        update((current) => completeIgnore(current, response));
      } catch (error) {
        update((current) => failIgnore(current, errorMessage(error)));
      }
    };

    const loadRating = async (target: GameRequests) => {
      try {
        const response = await apiRequest<GameRatingResponse>(target.rating, { onAuthFailure: logout, signal });
        update((current) => completeRating(current, response));
      } catch (error) {
        update((current) => failRating(current, errorMessage(error)));
      }
    };

    const loadFirstSessions = async () => {
      setState(beginFirstSessionsLoad);
      try {
        const response = await apiRequest<AccountSessionsResponse>(sessionsPagePath(requests, 1), {
          onAuthFailure: logout,
          signal,
        });
        if (!isCurrent()) return;
        setState((current) => completeFirstSessions(current, response));
      } catch (error) {
        if (!isCurrent()) return;
        setState((current) => failSessions(current, errorMessage(error)));
      }

      if (!isCurrent()) return;
      await Promise.all([loadIgnore(requests), loadRating(requests)]);
    };

    void Promise.all([loadDetails(), loadStats(), loadFirstSessions()]);

    return () => controller.abort();
  }, [hash]);

  const currentSignal = () => pageRequest.current?.signal;

  const loadSessionsPage = async (page: number) => {
    const requests = state.requests;
    if (requests === null) return;
    const signal = currentSignal();
    const isCurrent = () => signal?.aborted !== true;
    setState(beginRecentPageLoad);
    try {
      const response = await apiRequest<AccountSessionsResponse>(sessionsPagePath(requests, page), {
        onAuthFailure: logout,
        signal,
      });
      if (isCurrent()) setState((current) => completeSessionsPage(current, page, response));
    } catch (error) {
      if (isCurrent()) setState((current) => failSessions(current, errorMessage(error)));
    }
  };

  const toggleIgnore = async () => {
    if (state.requests === null) {
      setState((current) => failIgnore(current, "Cannot toggle without a game id."));
      return;
    }

    const submission = buildIgnoreSubmission(state, state.isIgnored);
    const signal = currentSignal();
    const isCurrent = () => signal?.aborted !== true;
    setState(beginIgnoreSave);
    try {
      const response = await apiRequest<GameIgnoreResponse>(submission.path, {
        method: submission.method,
        body: submission.body,
        onAuthFailure: logout,
        signal,
      });
      if (isCurrent()) setState((current) => completeIgnore(current, response));
    } catch (error) {
      if (isCurrent()) setState((current) => failIgnore(current, errorMessage(error)));
    } finally {
      if (isCurrent()) setState(endIgnoreSave);
    }
  };

  const rateGame = async (value: number | null) => {
    if (state.requests === null) {
      setState((current) => failRating(current, "Cannot rate without a game id."));
      return;
    }

    const submission = buildRatingSubmission(state, value);
    const signal = currentSignal();
    const isCurrent = () => signal?.aborted !== true;
    setState(beginRatingSave);
    try {
      const response = await apiRequest<GameRatingResponse>(submission.path, {
        method: submission.method,
        body: submission.body,
        onAuthFailure: logout,
        signal,
      });
      if (isCurrent()) setState((current) => completeRating(current, response));
    } catch (error) {
      if (isCurrent()) setState((current) => failRating(current, errorMessage(error)));
    } finally {
      if (isCurrent()) setState(endRatingSave);
    }
  };

  const changeYear = (event: ChangeEvent<HTMLSelectElement>) => {
    const value = event.target.value.trim();
    if (/^[+-]?\d+$/.test(value)) {
      const year = Number(value);
      if (Number.isSafeInteger(year)) setState((current) => setSelectedYear(current, year));
    }
  };

  const title = displayTitle(state);
  const image = gameImage(state);
  const ratingDisabled = isRatingDisabled(state);
  const totalPages = recentTotalPages(state);
  const years = yearlyStats(state);
  const months = monthlyStats(state);
  const selectedYear = activeYear(state);
  const now = new Date();

  return (
    <AppLayout title="Game">
      <div className="mt-6 rounded-2xl border border-emerald-400/30 dark:border-emerald-300/20 bg-gradient-to-r from-emerald-400/10 via-amber-300/20 to-white/40 dark:from-emerald-500/10 dark:via-amber-500/10 dark:to-slate-900/40 px-4 py-4 sm:px-6">
        <h2 className="bg-gradient-to-r from-slate-900 via-emerald-800 to-amber-700 bg-clip-text text-3xl font-semibold tracking-tight text-transparent dark:from-white dark:via-emerald-200 dark:to-amber-200 sm:text-4xl">
          {title}
        </h2>
      </div>

      {state.gameDetailsError.length > 0 && (
        <p className="mt-4 text-xs text-rose-700 dark:text-rose-300">{state.gameDetailsError}</p>
      )}

      <section className="mt-6 space-y-6">
        <div className="grid gap-6 lg:grid-cols-[1fr_2fr]">
          <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
            <div className="aspect-[9/14] overflow-hidden rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/60">
              {image !== null ? (
                <img src={image} alt={`${title} cover`} className="h-full w-full object-cover" />
              ) : (
                <div className="h-full w-full bg-slate-200/70 dark:bg-slate-800/60"></div>
              )}
            </div>

            <div className="mt-6 rounded-2xl border border-slate-200 dark:border-slate-800 bg-gradient-to-br from-amber-400/10 via-slate-200/70 dark:via-slate-950/70 to-emerald-500/10 p-4">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div>
                  <p className="text-xs uppercase tracking-[0.3em] text-amber-700 dark:text-amber-300">Your score</p>
                  {state.isRatingLoading && (
                    <p className="mt-1 text-sm text-slate-700 dark:text-slate-200">Loading rating...</p>
                  )}
                </div>
                <button
                  type="button"
                  className="rounded-full border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-950/70 px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.25em] text-slate-700 dark:text-slate-200 transition hover:border-amber-300/70 hover:text-amber-700 dark:hover:text-amber-200 disabled:cursor-not-allowed disabled:border-slate-200 dark:disabled:border-slate-800 disabled:text-slate-600"
                  onClick={() => void rateGame(null)}
                  disabled={ratingDisabled || state.rating === null}
                >
                  {state.isRatingSaving ? "Clearing..." : "Clear"}
                </button>
              </div>

              <div className="mt-4 flex flex-wrap items-center gap-3">
                <div className="flex items-center gap-2">
                  {[1, 2, 3, 4, 5].map((value) => {
                    const filled = state.rating !== null && value <= state.rating;
                    return (
                      <button
                        key={value}
                        type="button"
                        className={`flex h-11 w-11 items-center justify-center rounded-xl border text-sm font-semibold transition ${filled ? "border-amber-200 bg-amber-400/15 text-amber-700 dark:text-amber-100 shadow-[0_10px_40px_-20px_rgba(251,191,36,0.6)]" : "border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-950/70 text-slate-500 hover:border-amber-200/60 hover:text-amber-700 dark:hover:text-amber-200"} ${ratingDisabled ? "cursor-not-allowed opacity-60" : "hover:-translate-y-0.5"}`}
                        disabled={ratingDisabled}
                        onClick={() => void rateGame(value)}
                      >
                        <svg
                          aria-hidden="true"
                          viewBox="0 0 24 24"
                          className={`h-6 w-6 drop-shadow ${filled ? "text-amber-700 dark:text-amber-300" : "text-slate-600"}`}
                          fill={filled ? "currentColor" : "none"}
                          stroke="currentColor"
                          strokeWidth="1.5"
                        >
                          <path
                            d="M12 2.75 9.2 8.62l-6.2.54 4.7 4.06-1.4 5.98L12 16.9l5.7 3.3-1.4-5.98 4.7-4.06-6.2-.54z"
                            strokeLinecap="round"
                            strokeLinejoin="round"
                          />
                        </svg>
                        <span className="sr-only">Rate {value} out of 5</span>
                      </button>
                    );
                  })}
                </div>
                <span className="text-xs uppercase tracking-[0.2em] text-slate-500 dark:text-slate-400">
                  {state.isRatingLoading
                    ? "Loading rating..."
                    : state.rating === null
                      ? "Not rated"
                      : `${state.rating}/5 · ${ratingLabel(state)}`}
                </span>
              </div>

              {state.ratingError.length > 0 ? (
                <p className="mt-3 text-xs text-rose-700 dark:text-rose-300">{state.ratingError}</p>
              ) : state.requests === null && state.sessionsLoaded ? (
                <p className="mt-3 text-xs text-slate-500 dark:text-slate-400">Add a session for this game to unlock ratings.</p>
              ) : null}
            </div>
          </div>

          <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
            <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">Recent sessions</p>
            <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">Latest play snapshots</h2>

            <div className="mt-6 space-y-4">
              {state.gameError.length > 0 ? (
                <p className="text-xs text-rose-700 dark:text-rose-300">{state.gameError}</p>
              ) : state.isLoading ? (
                [1, 2, 3].map((item) => (
                  <div
                    key={item}
                    className="flex items-center gap-3 rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4"
                  >
                    <div className="h-14 w-10 rounded-xl bg-slate-200/70 dark:bg-slate-800/60"></div>
                    <div className="flex-1 space-y-2">
                      <div className="h-3 w-32 rounded-full bg-slate-200/70 dark:bg-slate-800/60"></div>
                      <div className="h-2 w-24 rounded-full bg-slate-200/40 dark:bg-slate-800/40"></div>
                    </div>
                    <div className="h-3 w-14 rounded-full bg-slate-200/70 dark:bg-slate-800/60"></div>
                  </div>
                ))
              ) : state.recentSessions.length === 0 ? (
                <div className="rounded-2xl border border-dashed border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4 text-sm text-slate-500 dark:text-slate-400">
                  No sessions logged for this game yet.
                </div>
              ) : (
                state.recentSessions.map((session) => (
                  <div
                    key={session.sessionId}
                    className="flex items-center justify-between gap-3 rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4"
                  >
                    <div className="flex items-center gap-3">
                      {session.capsuleUrl !== null ? (
                        <img
                          src={session.capsuleUrl}
                          alt={`${session.game} capsule art`}
                          className="h-14 w-10 rounded-xl border border-slate-200 dark:border-slate-800 object-cover"
                        />
                      ) : (
                        <div className="h-14 w-10 rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-200/70 dark:bg-slate-800/60"></div>
                      )}
                      <div>
                        <p className="text-sm font-semibold text-slate-900 dark:text-white">{formatDateLabel(session.startedAt)}</p>
                        <p className="text-xs text-slate-500 dark:text-slate-400">
                          {session.platformName} · {formatTimeLabel(session.startedAt)}
                        </p>
                      </div>
                    </div>
                    <p className="text-sm font-semibold text-emerald-700 dark:text-emerald-200">
                      {formatDuration(getDurationSeconds(session.startedAt, session.endedAt, now), "compact")}
                    </p>
                  </div>
                ))
              )}
            </div>

            {state.recentSessions.length > 0 && totalPages > 1 && (
              <div className="mt-6 flex items-center justify-between text-xs uppercase tracking-[0.2em] text-slate-500 dark:text-slate-400">
                <button
                  type="button"
                  className={pagerButtonClass}
                  onClick={() => void loadSessionsPage(state.recentPage - 1)}
                  disabled={state.recentPage === 1 || state.isRecentPageLoading}
                >
                  Prev
                </button>
                <span>
                  Page {state.recentPage} of {totalPages}
                </span>
                <button
                  type="button"
                  className={pagerButtonClass}
                  onClick={() => void loadSessionsPage(state.recentPage + 1)}
                  disabled={state.recentPage === totalPages || state.isRecentPageLoading}
                >
                  Next
                </button>
              </div>
            )}
          </div>
        </div>

        <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 px-4 py-4 sm:px-6 sm:py-5">
          <div className="flex flex-wrap items-center justify-between gap-4">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">Tracking</p>
              <p className="mt-2 text-sm text-slate-600 dark:text-slate-300">
                {state.requests === null
                  ? "Add a session for this game to enable tracking controls."
                  : state.isIgnored
                    ? "Scheduler will skip new sessions for this game."
                    : "Scheduler will capture new sessions for this game."}
              </p>
            </div>
            <div className="flex flex-wrap items-center gap-3">
              <span
                className={`rounded-full px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.25em] ${state.isIgnored ? "border border-rose-400/70 text-rose-700 dark:text-rose-100" : "border border-emerald-400/70 text-emerald-700 dark:text-emerald-100"}`}
              >
                {state.requests === null
                  ? "Unknown"
                  : state.isIgnoreChecking
                    ? "Checking..."
                    : state.isIgnored
                      ? "Ignored"
                      : "Active"}
              </span>
              <button
                type="button"
                className={`rounded-full px-4 py-2 text-[11px] font-semibold uppercase tracking-[0.2em] transition disabled:cursor-not-allowed disabled:opacity-60 ${state.isIgnored ? "border border-emerald-300/70 text-emerald-700 dark:text-emerald-100 hover:border-emerald-200" : "border border-rose-400/70 text-rose-700 dark:text-rose-100 hover:border-rose-300"}`}
                disabled={state.isIgnoreChecking || state.isIgnoreSaving || state.requests === null}
                onClick={() => void toggleIgnore()}
              >
                {state.isIgnoreSaving ? "Saving..." : state.isIgnored ? "Resume tracking" : "Ignore game"}
              </button>
            </div>
          </div>
          {state.ignoreError.length > 0 ? (
            <p className="mt-3 text-xs text-rose-700 dark:text-rose-300">{state.ignoreError}</p>
          ) : state.requests === null && state.sessionsLoaded ? (
            <p className="mt-3 text-xs text-slate-500 dark:text-slate-400">Add a session for this game to enable tracking controls.</p>
          ) : null}
        </div>

        <div className="grid grid-cols-1 gap-3 sm:grid-cols-3 sm:gap-6">
          <div className="flex items-center justify-between rounded-xl border border-slate-200 dark:border-slate-800 bg-white/90 dark:bg-slate-900/70 px-4 py-3 sm:block sm:space-y-2 sm:px-5">
            <p className="text-[11px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">Hours played</p>
            <p className="text-lg font-semibold text-slate-900 dark:text-white">{formatHours(totalSeconds(state), 1)}</p>
          </div>
          <div className="flex items-center justify-between rounded-xl border border-slate-200 dark:border-slate-800 bg-white/90 dark:bg-slate-900/70 px-4 py-3 sm:block sm:space-y-2 sm:px-5">
            <p className="text-[11px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">Sessions</p>
            <p className="text-lg font-semibold text-slate-900 dark:text-white">{state.stats?.sessionCount ?? 0}</p>
          </div>
          <div className="flex items-center justify-between rounded-xl border border-slate-200 dark:border-slate-800 bg-white/90 dark:bg-slate-900/70 px-4 py-3 sm:block sm:space-y-2 sm:px-5">
            <p className="text-[11px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">Last played</p>
            <p className="text-lg font-semibold text-slate-900 dark:text-white">{lastPlayedLabel(state)}</p>
          </div>
        </div>

        <div className="space-y-6">
          <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">All time since 2025</p>
                <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">Yearly play time</h2>
              </div>
            </div>
            {state.statsError.length > 0 ? (
              <p className="mt-4 text-xs text-rose-700 dark:text-rose-300">{state.statsError}</p>
            ) : state.stats === null ? (
              <div className="mt-6 grid grid-cols-[repeat(auto-fit,minmax(16px,1fr))] gap-2">
                {placeholderBars()}
              </div>
            ) : years.every((item) => item.seconds === 0) ? (
              <p className="mt-4 text-sm text-slate-500 dark:text-slate-400">No sessions recorded for this game since 2025.</p>
            ) : (
              <div className="mt-6 grid grid-cols-[repeat(auto-fit,minmax(16px,1fr))] items-end gap-2">
                {chartBars(years)}
              </div>
            )}
          </div>

          <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
            <div className="flex flex-wrap items-center justify-between gap-4">
              <div>
                <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">Year detail</p>
                <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">Monthly play time</h2>
              </div>
              <div className="flex items-center gap-3 rounded-full border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 px-4 py-2 text-xs uppercase tracking-[0.25em] text-slate-600 dark:text-slate-300">
                <span className="text-slate-500">Year</span>
                <select
                  className="bg-transparent text-slate-900 dark:text-slate-100 outline-none"
                  value={String(selectedYear)}
                  onChange={changeYear}
                >
                  {[...availableYears(state)].reverse().map((year) => (
                    <option key={year} value={year}>
                      {year}
                    </option>
                  ))}
                </select>
              </div>
            </div>
            {state.statsError.length > 0 ? (
              <p className="mt-4 text-xs text-rose-700 dark:text-rose-300">{state.statsError}</p>
            ) : state.stats === null ? (
              <div className="mt-6 grid grid-cols-12 gap-2">{placeholderBars()}</div>
            ) : months.every((item) => item.seconds === 0) ? (
              <p className="mt-4 text-sm text-slate-500 dark:text-slate-400">No sessions recorded in {selectedYear}.</p>
            ) : (
              <div className="mt-6 grid grid-cols-12 items-end gap-2">{chartBars(months)}</div>
            )}
          </div>
        </div>
      </section>
    </AppLayout>
  );
}

function placeholderBars() {
  return Array.from({ length: 12 }, (_, item) => (
    <div key={item} className="mx-auto h-32 w-2 rounded-md bg-slate-200/70 dark:bg-slate-800/60"></div>
  ));
}

function chartBars(buckets: GameChartBucket[]) {
  const maxHours = Math.max(Math.max(...buckets.map((item) => item.hours)), 1);
  return buckets.map((item) => {
    const hoursLabel = `${formatHours(item.seconds, 1)} played`;
    return (
      <div key={item.key} className="group relative flex flex-col gap-3">
        <div className={tooltipClass}>{hoursLabel}</div>
        <div className="flex h-32 items-end justify-center">
          <div
            aria-label={`${hoursLabel} in ${item.label}`}
            title={hoursLabel}
            className="w-2 rounded-md bg-emerald-400/80"
            style={{ height: `${String((item.hours / maxHours) * 100)}%` }}
          ></div>
        </div>
        <div className="text-center text-[10px] uppercase tracking-[0.2em] text-slate-500 dark:text-slate-400">{item.label}</div>
      </div>
    );
  });
}
