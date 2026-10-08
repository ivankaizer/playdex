import { useEffect, useRef, useState, type ChangeEvent } from "react";
import { AppLayout } from "@/components/AppLayout";
import { BarChart, CardHeading, Cover, Dot, StatTile, type BarDatum } from "@/components/ui";
import { apiRequest, errorMessage } from "@/lib/api";
import { getDurationSeconds } from "@/lib/dates";
import { formatDateLabel, formatDuration, formatHours, formatTimeLabel } from "@/lib/format";
import { cx } from "@/lib/cx";
import { logout } from "@/lib/navigation";
import { platformColor } from "@/lib/platforms";
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

  const trackingLabel =
    state.requests === null ? "Unknown" : state.isIgnoreChecking ? "Checking..." : state.isIgnored ? "Ignored" : "Active";
  const yearsEmpty = years.every((item) => item.seconds === 0);
  const monthsEmpty = months.every((item) => item.seconds === 0);

  return (
    <AppLayout title="Game">
      <section className="pdx-card">
        <div className="grid gap-10 md:grid-cols-[minmax(0,240px)_minmax(0,1fr)]">
          <div
            role={image !== null ? "img" : undefined}
            aria-label={image !== null ? `${title} cover` : undefined}
            className="w-full max-w-[240px]"
          >
            <Cover
              title={title}
              src={image}
              className="aspect-[2/3] w-full rounded-[22px] shadow-cover"
              style={{ background: "linear-gradient(160deg,#2b3540,#5a6672)" }}
              initialClassName="text-[40px]"
            />
          </div>

          <div className="flex min-w-0 flex-col gap-7">
            <div className="min-w-0">
              <p className="pdx-kicker">Game</p>
              <h2 className="mt-2 break-words text-[40px] font-semibold leading-[1.08] tracking-[-0.025em] text-ink">{title}</h2>
              {state.gameDetailsError.length > 0 && (
                <p className="mt-3 text-[13px] text-danger">{state.gameDetailsError}</p>
              )}
            </div>

            <div className="grid grid-cols-[repeat(auto-fit,minmax(min(170px,100%),1fr))] gap-4">
              <StatTile label="Hours played" value={formatHours(totalSeconds(state), 1)} />
              <StatTile label="Sessions" value={state.stats?.sessionCount ?? 0} />
              <StatTile label="Last played" value={lastPlayedLabel(state)} />
            </div>

            <div className="grid grid-cols-[repeat(auto-fit,minmax(min(280px,100%),1fr))] gap-4">
              <div className="pdx-tile flex flex-col gap-4">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div>
                    <p className="pdx-label">Your score</p>
                    {state.isRatingLoading && <p className="mt-1 text-[13px] text-muted">Loading rating...</p>}
                  </div>
                  <button
                    type="button"
                    className="pdx-btn-outline !h-8 !px-3.5 text-xs"
                    onClick={() => void rateGame(null)}
                    disabled={ratingDisabled || state.rating === null}
                  >
                    {state.isRatingSaving ? "Clearing..." : "Clear"}
                  </button>
                </div>

                <div className="flex flex-wrap items-center gap-x-4 gap-y-3">
                  <div className="flex items-center gap-1.5">
                    {[1, 2, 3, 4, 5].map((value) => {
                      const filled = state.rating !== null && value <= state.rating;
                      return (
                        <button
                          key={value}
                          type="button"
                          aria-pressed={filled}
                          className={cx(
                            "grid h-10 w-10 place-items-center rounded-[14px] border transition",
                            filled ? "border-mint-line bg-mint text-accent" : "border-line bg-surface text-faint hover:text-accent",
                            ratingDisabled ? "cursor-not-allowed opacity-60" : "hover:border-mint-hover",
                          )}
                          disabled={ratingDisabled}
                          onClick={() => void rateGame(value)}
                        >
                          <svg
                            aria-hidden="true"
                            viewBox="0 0 24 24"
                            className="h-5 w-5"
                            fill={filled ? "currentColor" : "none"}
                            stroke="currentColor"
                            strokeWidth="1.6"
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
                  <span className="pdx-num text-sm text-ink-2">
                    {state.isRatingLoading ? (
                      "Loading rating..."
                    ) : state.rating === null ? (
                      "Not rated"
                    ) : (
                      <>
                        <span className="font-semibold text-ink">{state.rating}/5</span> · {ratingLabel(state)}
                      </>
                    )}
                  </span>
                </div>

                {state.ratingError.length > 0 ? (
                  <p className="text-[13px] text-danger">{state.ratingError}</p>
                ) : state.requests === null && state.sessionsLoaded ? (
                  <p className="text-[13px] text-muted">Add a session for this game to unlock ratings.</p>
                ) : null}
              </div>

              <div className="pdx-tile flex flex-col gap-4">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <p className="pdx-label">Tracking</p>
                  <span
                    className={cx(
                      "inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.18em]",
                      state.requests === null || state.isIgnoreChecking
                        ? "border-line text-muted"
                        : state.isIgnored
                          ? "border-danger-line bg-danger-tint text-danger"
                          : "border-mint-line bg-mint text-accent",
                    )}
                  >
                    {trackingLabel}
                  </span>
                </div>
                <p className="text-sm text-ink-2">
                  {state.requests === null
                    ? "Add a session for this game to enable tracking controls."
                    : state.isIgnored
                      ? "Scheduler will skip new sessions for this game."
                      : "Scheduler will capture new sessions for this game."}
                </p>
                <div>
                  <button
                    type="button"
                    className={state.isIgnored ? "pdx-btn-accent" : "pdx-btn-danger"}
                    disabled={state.isIgnoreChecking || state.isIgnoreSaving || state.requests === null}
                    onClick={() => void toggleIgnore()}
                  >
                    {state.isIgnoreSaving ? "Saving..." : state.isIgnored ? "Resume tracking" : "Ignore game"}
                  </button>
                </div>
                {state.ignoreError.length > 0 ? (
                  <p className="text-[13px] text-danger">{state.ignoreError}</p>
                ) : state.requests === null && state.sessionsLoaded ? (
                  <p className="text-[13px] text-muted">Add a session for this game to enable tracking controls.</p>
                ) : null}
              </div>
            </div>
          </div>
        </div>
      </section>

      <section className="pdx-card flex flex-col gap-6">
        <CardHeading kicker="Recent sessions" title="Latest play snapshots" />

        <div className="flex flex-col gap-1">
          {state.gameError.length > 0 ? (
            <p className="text-[13px] text-danger">{state.gameError}</p>
          ) : state.isLoading ? (
            [1, 2, 3].map((item) => (
              <div key={item} aria-hidden="true" className="-mx-3 grid grid-cols-[48px_minmax(0,1fr)_auto] items-center gap-4 px-3 py-2.5">
                <div className="h-12 w-12 rounded-[14px] bg-track"></div>
                <div className="space-y-2">
                  <div className="h-3 w-32 max-w-full rounded-full bg-track"></div>
                  <div className="h-2 w-24 max-w-full rounded-full bg-tile"></div>
                </div>
                <div className="h-3 w-14 rounded-full bg-track"></div>
              </div>
            ))
          ) : state.recentSessions.length === 0 ? (
            <p className="pdx-dashed">No sessions logged for this game yet.</p>
          ) : (
            state.recentSessions.map((session) => (
              <div
                key={session.sessionId}
                className="pdx-row -mx-3 grid grid-cols-[48px_minmax(0,1fr)_auto] items-center gap-4 px-3 py-2.5"
              >
                <Cover
                  title={session.game}
                  src={session.capsuleUrl}
                  className="h-12 w-12 rounded-[14px] shadow-[0_4px_10px_-4px_rgba(30,50,70,.35)]"
                />
                <div className="min-w-0">
                  <p className="truncate text-[15px] font-medium text-ink">{formatDateLabel(session.startedAt)}</p>
                  <p className="mt-0.5 flex items-center gap-1.5 text-[13px] text-muted">
                    <Dot color={platformColor(session.platformKey, session.platformName)} size={7} />
                    <span className="truncate">
                      {session.platformName} · {formatTimeLabel(session.startedAt)}
                    </span>
                  </p>
                </div>
                <span className="pdx-num text-[15px] font-semibold text-accent">
                  {formatDuration(getDurationSeconds(session.startedAt, session.endedAt, now), "compact")}
                </span>
              </div>
            ))
          )}
        </div>

        {state.recentSessions.length > 0 && totalPages > 1 && (
          <div className="flex flex-wrap items-center justify-between gap-3">
            <button
              type="button"
              className="pdx-btn-outline"
              onClick={() => void loadSessionsPage(state.recentPage - 1)}
              disabled={state.recentPage === 1 || state.isRecentPageLoading}
            >
              ← Prev
            </button>
            <span className="pdx-num text-[13px] text-muted">
              Page {state.recentPage} of {totalPages}
            </span>
            <button
              type="button"
              className="pdx-btn-outline"
              onClick={() => void loadSessionsPage(state.recentPage + 1)}
              disabled={state.recentPage === totalPages || state.isRecentPageLoading}
            >
              Next →
            </button>
          </div>
        )}
      </section>

      <section className="pdx-card flex flex-col gap-7">
        <CardHeading kicker="Activity" title="Play time over time" />

        <div className="grid grid-cols-[repeat(auto-fit,minmax(min(320px,100%),1fr))] gap-4">
          <div className="pdx-tile flex flex-col gap-3.5 !p-7">
            <div className="flex flex-wrap items-baseline justify-between gap-3">
              <h3 className="pdx-label">Yearly play time</h3>
              <span className="text-[13px] text-muted">All time since 2025</span>
            </div>
            {state.statsError.length > 0 ? (
              <p className="text-[13px] text-danger">{state.statsError}</p>
            ) : state.stats === null ? (
              <BarChart data={placeholderBars(3)} height={130} />
            ) : yearsEmpty ? (
              <p className="pdx-dashed">No sessions recorded for this game since 2025.</p>
            ) : (
              <BarChart data={chartBars(years)} height={130} radius={10} gap={12} ariaLabel="Yearly play time" />
            )}
          </div>

          <div className="pdx-tile flex flex-col gap-3.5 !p-7">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <h3 className="pdx-label">Monthly play time</h3>
              <label className="flex items-center gap-2 text-[13px] text-muted">
                Year
                <select className="pdx-input !h-8 !w-auto !pr-8 text-[13px]" value={String(selectedYear)} onChange={changeYear}>
                  {[...availableYears(state)].reverse().map((year) => (
                    <option key={year} value={year}>
                      {year}
                    </option>
                  ))}
                </select>
              </label>
            </div>
            {state.statsError.length > 0 ? (
              <p className="text-[13px] text-danger">{state.statsError}</p>
            ) : state.stats === null ? (
              <BarChart data={placeholderBars(12)} height={130} />
            ) : monthsEmpty ? (
              <p className="pdx-dashed">No sessions recorded in {selectedYear}.</p>
            ) : (
              <BarChart data={chartBars(months)} height={130} radius={6} gap={6} ariaLabel={`Monthly play time in ${selectedYear}`} />
            )}
          </div>
        </div>
      </section>
    </AppLayout>
  );
}

function placeholderBars(count: number): BarDatum[] {
  return Array.from({ length: count }, (_, item) => ({ key: String(item), value: 0, label: " " }));
}

/** Maps chart buckets to bars; the peak bucket is highlighted in green. */
function chartBars(buckets: GameChartBucket[]): BarDatum[] {
  const peak = Math.max(...buckets.map((item) => item.seconds), 0);
  return buckets.map((item) => {
    const hoursLabel = `${formatHours(item.seconds, 1)} played`;
    return {
      key: item.key,
      value: item.hours,
      label: item.label,
      title: `${hoursLabel} in ${item.label}`,
      highlight: peak > 0 && item.seconds === peak,
    };
  });
}
