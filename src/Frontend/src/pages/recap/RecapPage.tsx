import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useLocation } from "react-router";
import { AppLayout } from "@/components/AppLayout";
import { apiRequest, errorMessage } from "@/lib/api";
import { formatHours } from "@/lib/format";
import { encodeGameId } from "@/lib/gameHash";
import { logout, useAppNavigate } from "@/lib/navigation";
import type { RecapResponse, RecapYearsResponse } from "./recapModels";
import {
  applyLocationQuery,
  beginLoad,
  beginYearsLoad,
  buildCustomNavigation,
  completeLoad,
  completeYears,
  failLoad,
  failYears,
  initialRecapState,
  recapTabs,
  recapYearsPath,
  runInitialCustomFlow,
  selectTab,
  setCustomFrom,
  setCustomTo,
  takeDefaultRequest,
  type RecapState,
  type RecapStep,
} from "./recapState";
import {
  buildWeekdayChart,
  cadenceStats,
  formatNumber,
  gameHighlightStats,
  gameMixStats,
  rangeLabel,
  roundPercent,
  sessionLengthStats,
  volumeStats,
  type LabeledValue,
} from "./recapView";

const WeekdayLabels = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
const PlatformBarClasses = ["bg-emerald-400", "bg-teal-400", "bg-cyan-400", "bg-sky-400", "bg-indigo-400", "bg-violet-400"];
const PlatformDotClasses = [
  "bg-emerald-400/80",
  "bg-teal-400/80",
  "bg-cyan-400/80",
  "bg-sky-400/80",
  "bg-indigo-400/80",
  "bg-violet-400/80",
];

const percentStyle = (value: number) => ({ width: `${formatNumber(value)}%` });
const pad2 = (value: number) => String(value).padStart(2, "0");

export function RecapPage() {
  const location = useLocation();
  const navigate = useAppNavigate();
  const [state, setState] = useState<RecapState>(initialRecapState);
  const stateRef = useRef(state);
  const lifetimeRef = useRef<AbortController | null>(null);
  const recapRequestRef = useRef<AbortController | null>(null);
  const recapVersionRef = useRef(0);
  const initializedRef = useRef(false);

  const apply = useCallback((transition: (current: RecapState) => RecapState) => {
    stateRef.current = transition(stateRef.current);
    setState(stateRef.current);
  }, []);

  const step = useCallback(<R,>(transition: (current: RecapState) => RecapStep<R>): R => {
    const [next, result] = transition(stateRef.current);
    stateRef.current = next;
    setState(next);
    return result;
  }, []);

  const loadYears = useCallback(
    async (lifetime: AbortController) => {
      apply(beginYearsLoad);
      try {
        const response = await apiRequest<RecapYearsResponse>(recapYearsPath, {
          onAuthFailure: logout,
          signal: lifetime.signal,
        });
        if (!lifetime.signal.aborted) apply((current) => completeYears(current, response));
      } catch (error) {
        if (!lifetime.signal.aborted) apply((current) => failYears(current, errorMessage(error)));
      }
    },
    [apply],
  );

  const loadRecap = useCallback(
    async (lifetime: AbortController, path: string) => {
      if (lifetime.signal.aborted) return;
      recapRequestRef.current?.abort();
      const request = new AbortController();
      const abortRequest = () => request.abort();
      lifetime.signal.addEventListener("abort", abortRequest, { once: true });
      recapRequestRef.current = request;
      const version = ++recapVersionRef.current;
      apply(beginLoad);

      try {
        const response = await apiRequest<RecapResponse>(path, { onAuthFailure: logout, signal: request.signal });
        if (version === recapVersionRef.current) apply((current) => completeLoad(current, response));
      } catch (error) {
        if (request.signal.aborted) return;
        if (version === recapVersionRef.current) apply((current) => failLoad(current, errorMessage(error)));
      } finally {
        lifetime.signal.removeEventListener("abort", abortRequest);
        if (recapRequestRef.current === request) recapRequestRef.current = null;
      }
    },
    [apply],
  );

  const takeDefault = useCallback(
    (lifetime: AbortController) => (lifetime.signal.aborted ? null : step(takeDefaultRequest)),
    [step],
  );

  // Component lifetime (re-created on StrictMode remount, which also resets the page state).
  useEffect(() => {
    const lifetime = new AbortController();
    lifetimeRef.current = lifetime;
    initializedRef.current = false;
    return () => {
      lifetime.abort();
      recapRequestRef.current?.abort();
      recapRequestRef.current = null;
      stateRef.current = initialRecapState;
    };
  }, []);

  // Equivalent of OnParametersSetAsync: re-runs when the `from`/`to` query values change.
  const queryKey = useMemo(() => {
    const params = new URLSearchParams(location.search);
    return JSON.stringify([params.get("from"), params.get("to")]);
  }, [location.search]);

  useEffect(() => {
    const lifetime = lifetimeRef.current;
    if (lifetime === null) return;
    const firstRender = !initializedRef.current;
    initializedRef.current = true;
    const request = step((current) => applyLocationQuery(current, location.search));

    const run = async () => {
      if (firstRender) {
        if (request !== null) {
          await runInitialCustomFlow(
            request,
            () => loadYears(lifetime),
            () => takeDefault(lifetime),
            (path) => loadRecap(lifetime, path),
          );
          return;
        }

        await loadYears(lifetime);
        const defaultRequest = takeDefault(lifetime);
        if (defaultRequest !== null) await loadRecap(lifetime, defaultRequest);
        return;
      }

      if (request !== null) {
        await loadRecap(lifetime, request);
        return;
      }

      const fallbackRequest = takeDefault(lifetime);
      if (fallbackRequest !== null) await loadRecap(lifetime, fallbackRequest);
    };

    void run();
  }, [queryKey, step, loadYears, loadRecap, takeDefault]);

  const handleSelectTab = (key: string) => {
    const request = step((current) => selectTab(current, key));
    const lifetime = lifetimeRef.current;
    if (request !== null && lifetime !== null) void loadRecap(lifetime, request);
  };

  const applyCustom = () => {
    const target = buildCustomNavigation(stateRef.current);
    if (target === null) return;
    navigate(target);
  };

  const navigateToGame = (gameId: string) => navigate(`/games/${encodeGameId(gameId)}`);

  const tabClass = (key: string) =>
    "rounded-full border px-3 py-2 text-[10px] font-semibold uppercase tracking-[0.25em] transition " +
    (state.activeTab === key
      ? "border-emerald-300 bg-emerald-400 text-slate-900"
      : "border-slate-200 dark:border-slate-800 text-slate-600 dark:text-slate-200 hover:border-emerald-300/60");

  const recap = state.recap;
  const weekdayChart = buildWeekdayChart(recap?.sessionsByWeekday ?? []);
  const hourMaximum = Math.max(1, ...(recap?.sessionsByHour.map((item) => item.sessionCount) ?? [0]));
  const daysPlayed = recap?.daysPlayed ?? 0;
  const daysNotPlayed = recap?.daysNotPlayed ?? 0;
  const totalDays = daysPlayed + daysNotPlayed;
  const daysPlayedPercent = totalDays > 0 ? (daysPlayed / totalDays) * 100 : 0;
  const daysPlayedPercentRounded = roundPercent(daysPlayedPercent);
  const platformTotalSeconds = recap?.platformTotals.reduce((sum, item) => sum + item.totalSeconds, 0) ?? 0;
  const timeSplitTotalSeconds = (recap?.weekendSeconds ?? 0) + (recap?.weekdaySeconds ?? 0);
  const weekendShare = timeSplitTotalSeconds > 0 ? (recap?.weekendSeconds ?? 0) / timeSplitTotalSeconds : 0;
  const weekdayShare = timeSplitTotalSeconds > 0 ? (recap?.weekdaySeconds ?? 0) / timeSplitTotalSeconds : 0;
  const daysStrokeDasharray = `${formatNumber(daysPlayedPercent)} ${formatNumber(100 - daysPlayedPercent)}`;
  const weekdayViewBox = `0 0 ${formatNumber(weekdayChart.width)} ${formatNumber(weekdayChart.height)}`;

  return (
    <AppLayout title="Recap">
      <section className="mt-10 space-y-6">
        <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-4 sm:p-5">
          <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">Recap tabs</p>
          <div className="mt-4 flex flex-wrap gap-2">
            {recapTabs(state).map((tab) => (
              <button key={tab.key} type="button" onClick={() => handleSelectTab(tab.key)} className={tabClass(tab.key)}>
                {tab.label}
              </button>
            ))}
          </div>
        </div>

        <div className="space-y-6">
          {state.activeTab === "custom" && (
            <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                    Custom recap
                  </p>
                  <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">Choose your range</h2>
                </div>
              </div>
              <div className="mt-5 grid gap-3 md:grid-cols-[1fr_1fr_auto]">
                <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
                  From{" "}
                  <input
                    type="date"
                    value={state.customFrom}
                    onChange={(event) => apply((current) => setCustomFrom(current, event.target.value))}
                    className="mt-2 w-full rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                  />
                </label>
                <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
                  To{" "}
                  <input
                    type="date"
                    value={state.customTo}
                    onChange={(event) => apply((current) => setCustomTo(current, event.target.value))}
                    className="mt-2 w-full rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                  />
                </label>
                <button
                  type="button"
                  onClick={applyCustom}
                  className="mt-6 rounded-full border border-emerald-400/60 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-200 transition hover:border-emerald-300"
                >
                  Apply
                </button>
              </div>
            </div>
          )}

          {state.error.length > 0 && <p className="text-xs text-rose-700 dark:text-rose-300">{state.error}</p>}

          {recap !== null && (
            <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                    Most popular
                  </p>
                  <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">Top 5 games</h2>
                </div>
                <div className="text-xs font-semibold uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                  {rangeLabel(recap)}
                </div>
              </div>
              <div className="mt-5 grid items-start gap-3 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-5">
                {recap.topGamesByTime.length === 0 ? (
                  <div className="rounded-2xl border border-dashed border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4 text-sm text-slate-500 dark:text-slate-400 lg:col-span-5">
                    No game data available yet.
                  </div>
                ) : (
                  recap.topGamesByTime.map((game, index) => (
                    <button key={index} type="button" onClick={() => navigateToGame(game.gameId)} className="group text-left">
                      <div className="w-full">
                        <div className="overflow-hidden rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50">
                          {game.capsuleUrl ? (
                            <img src={game.capsuleUrl} alt={`${game.title} capsule art`} className="aspect-[2/3] w-full object-cover" />
                          ) : (
                            <div className="flex aspect-[2/3] w-full items-center justify-center bg-slate-200/70 dark:bg-slate-900/70 text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                              No art
                            </div>
                          )}
                        </div>
                        <div className="mt-3 grid grid-cols-[1fr_auto] items-start gap-3 text-sm">
                          <span className="min-w-0 line-clamp-2 font-semibold leading-snug text-slate-900 dark:text-white group-hover:text-emerald-700 dark:group-hover:text-emerald-200">
                            {game.title}
                          </span>
                          <span className="text-xs font-semibold text-emerald-700 dark:text-emerald-200">
                            {formatHours(game.totalSeconds, 1)}
                          </span>
                        </div>
                      </div>
                    </button>
                  ))
                )}
              </div>
            </div>
          )}

          {state.isLoading && <p className="text-xs text-slate-500 dark:text-slate-400">Loading recap...</p>}

          {recap !== null && (
            <>
              <div className="grid gap-6 lg:grid-cols-2 lg:items-stretch">
                <div className="h-full rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                  <div className="flex flex-wrap items-center justify-between gap-3">
                    <div>
                      <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                        Session overview
                      </p>
                      <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">Volume and session length</h2>
                    </div>
                  </div>
                  <div className="mt-4 grid gap-4">
                    <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/40 p-4">
                      <p className="text-xs font-semibold uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">Volume</p>
                      <div className="mt-3 grid gap-2 text-sm">{statRows(volumeStats(recap))}</div>
                    </div>
                    <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/40 p-4">
                      <p className="text-xs font-semibold uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                        Session length
                      </p>
                      <div className="mt-3 grid gap-2 text-sm">{statRows(sessionLengthStats(recap))}</div>
                    </div>
                  </div>
                </div>

                <div className="h-full rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                  <div className="flex flex-wrap items-center justify-between gap-3">
                    <div>
                      <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                        Consistency
                      </p>
                      <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">Days played</h2>
                    </div>
                    <div className="text-xs font-semibold uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                      {daysPlayed} / {totalDays} days
                    </div>
                  </div>
                  <div className="mt-4 grid gap-4 sm:grid-cols-[auto_1fr] items-center">
                    <div className="relative flex h-36 w-36 items-center justify-center">
                      <svg className="h-full w-full -rotate-90" viewBox="0 0 120 120">
                        <circle
                          cx="60"
                          cy="60"
                          r="46"
                          fill="none"
                          stroke="currentColor"
                          strokeWidth="12"
                          className="text-slate-200 dark:text-slate-800"
                        ></circle>
                        <circle
                          cx="60"
                          cy="60"
                          r="46"
                          fill="none"
                          stroke="currentColor"
                          strokeWidth="12"
                          strokeLinecap="round"
                          strokeDasharray={daysStrokeDasharray}
                          pathLength="100"
                          className="text-emerald-400"
                        ></circle>
                      </svg>
                      <div className="absolute text-center">
                        <p className="text-2xl font-semibold text-slate-900 dark:text-white">{daysPlayedPercentRounded}%</p>
                        <p className="text-[10px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">played</p>
                      </div>
                    </div>
                    <div className="grid gap-2 text-sm">
                      <div className="flex items-center justify-between">
                        <span className="text-[10px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">Days played</span>
                        <span className="font-semibold text-slate-900 dark:text-white">{daysPlayed}</span>
                      </div>
                      <div className="flex items-center justify-between">
                        <span className="text-[10px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">Days off</span>
                        <span className="font-semibold text-slate-900 dark:text-white">{daysNotPlayed}</span>
                      </div>
                      <div className="flex items-center justify-between">
                        <span className="text-[10px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">Play rate</span>
                        <span className="font-semibold text-slate-900 dark:text-white">{daysPlayedPercentRounded}%</span>
                      </div>
                    </div>
                  </div>
                  <div className="mt-5 grid gap-3 sm:grid-cols-2">{statCards(cadenceStats(recap))}</div>
                </div>
              </div>

              <div className="grid gap-6 lg:grid-cols-2 lg:items-stretch">
                <div className="h-full rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                  <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">Weekly rhythm</p>
                  <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">Sessions by weekday</h2>
                  <div className="mt-4">
                    <svg className="h-24 w-full" viewBox={weekdayViewBox} preserveAspectRatio="none">
                      <defs>
                        <linearGradient id="weekdayGradient" x1="0" x2="0" y1="0" y2="1">
                          <stop offset="0%" stopColor="rgba(52, 211, 153, 0.4)"></stop>
                          <stop offset="100%" stopColor="rgba(52, 211, 153, 0)"></stop>
                        </linearGradient>
                      </defs>
                      {weekdayChart.areaPath.length > 0 && (
                        <path d={weekdayChart.areaPath} fill="url(#weekdayGradient)" stroke="none"></path>
                      )}
                      {weekdayChart.linePoints.length > 0 && (
                        <polyline
                          points={weekdayChart.linePoints}
                          fill="none"
                          stroke="rgba(16, 185, 129, 0.9)"
                          strokeWidth="2.5"
                          strokeLinecap="round"
                          strokeLinejoin="round"
                        ></polyline>
                      )}
                      {weekdayChart.points.map((point, index) => (
                        <circle
                          key={index}
                          cx={formatNumber(point.x)}
                          cy={formatNumber(point.y)}
                          r="2.5"
                          fill="rgba(16, 185, 129, 0.9)"
                        ></circle>
                      ))}
                    </svg>
                    <div className="mt-3 grid grid-cols-7 text-[10px] uppercase tracking-[0.2em] text-slate-500 dark:text-slate-400">
                      {WeekdayLabels.map((label) => (
                        <span key={label} className="text-center">
                          {label}
                        </span>
                      ))}
                    </div>
                  </div>
                </div>

                <div className="h-full rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                  <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                    Hourly activity
                  </p>
                  <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">Heatmap by hour</h2>
                  <div className="mt-4 grid grid-cols-12 gap-2">
                    {recap.sessionsByHour.map((item, index) => {
                      const intensity = item.sessionCount / hourMaximum;
                      const opacity = 0.2 + intensity * 0.8;
                      return (
                        <div key={index} className="flex flex-col items-center gap-2">
                          <span className="h-3 w-3 rounded-full bg-emerald-400" style={{ opacity: formatNumber(opacity) }}></span>
                          <span className="text-[9px] uppercase tracking-[0.2em] text-slate-500 dark:text-slate-400">
                            {pad2(item.hour)}
                          </span>
                        </div>
                      );
                    })}
                  </div>
                  <div className="mt-4 text-xs text-slate-500 dark:text-slate-400">
                    Peak hour:{" "}
                    <span className="font-semibold text-slate-900 dark:text-white">
                      {recap.peakHour ? `${pad2(recap.peakHour.hour)}:00` : "—"}
                    </span>
                  </div>
                </div>
              </div>

              <div className="grid gap-6 lg:grid-cols-2 lg:items-stretch">
                <div className="h-full rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                  <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">Time split</p>
                  <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">Weekend vs weekday</h2>
                  <div className="mt-4 space-y-3">
                    <div className="flex items-center justify-between text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                      <span>Weekend</span>
                      <span className="font-semibold text-slate-900 dark:text-white">
                        {formatHours(recap.weekendSeconds, 1)} · {roundPercent(weekendShare * 100)}%
                      </span>
                    </div>
                    <div className="flex h-3 w-full overflow-hidden rounded-full bg-slate-200/70 dark:bg-slate-800/70">
                      <div className="h-full bg-emerald-400" style={percentStyle(weekendShare * 100)}></div>
                      <div className="h-full bg-sky-400" style={percentStyle(weekdayShare * 100)}></div>
                    </div>
                    <div className="flex items-center justify-between text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                      <span>Weekday</span>
                      <span className="font-semibold text-slate-900 dark:text-white">
                        {formatHours(recap.weekdaySeconds, 1)} · {roundPercent(weekdayShare * 100)}%
                      </span>
                    </div>
                  </div>
                  <div className="mt-6 grid gap-3 sm:grid-cols-2">
                    <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/40 p-3">
                      <p className="text-[10px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">Peak hour</p>
                      <p className="mt-2 text-sm font-semibold text-slate-900 dark:text-white">
                        {recap.peakHour ? `${pad2(recap.peakHour.hour)}:00 · ${recap.peakHour.sessionCount}` : "—"}
                      </p>
                    </div>
                    <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/40 p-3">
                      <p className="text-[10px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">Most used platform</p>
                      <p className="mt-2 text-sm font-semibold text-slate-900 dark:text-white">
                        {recap.mostUsedPlatform ? recap.mostUsedPlatform.platformName : "—"}
                      </p>
                    </div>
                  </div>
                </div>

                <div className="h-full rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                  <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">Platform mix</p>
                  <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">Share of playtime</h2>
                  <div className="mt-5 space-y-4">
                    {recap.platformTotals.length === 0 ? (
                      <p className="text-sm text-slate-500 dark:text-slate-400">No platform data available yet.</p>
                    ) : (
                      recap.platformTotals.map((item, index) => {
                        const share = platformTotalSeconds > 0 ? (item.totalSeconds / platformTotalSeconds) * 100 : 0;
                        const barClass = PlatformBarClasses[index % PlatformBarClasses.length];
                        const dotClass = PlatformDotClasses[index % PlatformDotClasses.length];
                        return (
                          <div key={index} className="space-y-2">
                            <div className="flex items-center justify-between text-sm">
                              <div className="flex items-center gap-2">
                                <span className={`h-2.5 w-2.5 rounded-full ${dotClass}`}></span>
                                <span className="font-semibold text-slate-900 dark:text-white">{item.platformName}</span>
                              </div>
                              <span className="text-xs font-semibold text-slate-500 dark:text-slate-400">{roundPercent(share)}%</span>
                            </div>
                            <div className="flex items-center justify-between text-xs text-slate-500 dark:text-slate-400">
                              <span>{item.sessionCount} sessions</span>
                              <span className="font-semibold text-emerald-700 dark:text-emerald-200">
                                {formatHours(item.totalSeconds, 1)}
                              </span>
                            </div>
                            <div className="h-2 w-full overflow-hidden rounded-full bg-slate-200/70 dark:bg-slate-800/70">
                              <div className={`h-full rounded-full ${barClass}`} style={percentStyle(share)}></div>
                            </div>
                          </div>
                        );
                      })
                    )}
                  </div>
                </div>
              </div>

              <div className="grid gap-6 lg:grid-cols-2 lg:items-stretch">
                <div className="h-full rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                  <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">Library mix</p>
                  <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">How you explore games</h2>
                  <div className="mt-4 grid gap-3 sm:grid-cols-2">{statCards(gameMixStats(recap))}</div>
                </div>

                <div className="h-full rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                  <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">Highlights</p>
                  <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">Game leaders</h2>
                  <div className="mt-4 grid gap-3">{statCards(gameHighlightStats(recap))}</div>
                </div>
              </div>

              <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                      Full library
                    </p>
                    <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">Summed playtime by game</h2>
                  </div>
                </div>
                <div className="mt-5 max-h-96 overflow-auto pr-1">
                  {recap.gamesTable.length === 0 ? (
                    <p className="text-sm text-slate-500 dark:text-slate-400">No games logged yet.</p>
                  ) : (
                    <div className="overflow-hidden rounded-2xl border border-slate-200 dark:border-slate-800">
                      <table className="w-full text-left text-xs">
                        <thead className="bg-white/80 dark:bg-slate-950/70 text-[10px] uppercase tracking-[0.28em] text-slate-500 dark:text-slate-400">
                          <tr>
                            <th className="px-3 py-2 text-left">Game</th>
                            <th className="px-3 py-2 text-left">Sessions</th>
                            <th className="px-3 py-2 text-left">Time</th>
                          </tr>
                        </thead>
                        <tbody>
                          {recap.gamesTable.map((game, index) => (
                            <tr
                              key={index}
                              className="border-t border-slate-200/70 dark:border-slate-800/80 bg-slate-200/30 dark:bg-slate-950/40"
                            >
                              <td className="px-3 py-2">
                                <div className="flex min-w-0 items-center gap-2">
                                  <span className="text-[10px] font-semibold text-slate-500 dark:text-slate-400">{pad2(index + 1)}</span>
                                  <div className="flex min-w-0 items-center gap-2">
                                    <button
                                      type="button"
                                      onClick={() => navigateToGame(game.gameId)}
                                      title={game.title}
                                      className="min-w-0 flex-1 truncate text-left text-sm font-semibold text-slate-900 dark:text-white transition hover:text-emerald-700 dark:hover:text-emerald-200"
                                    >
                                      {game.title}
                                    </button>
                                    {game.isAbandoned && (
                                      <span className="inline-flex items-center rounded-full border border-slate-300/70 bg-slate-200/40 px-1.5 py-px text-[8px] font-medium uppercase tracking-[0.14em] text-slate-500 dark:border-slate-700/80 dark:bg-slate-800/30 dark:text-slate-400">
                                        abandoned
                                      </span>
                                    )}
                                  </div>
                                </div>
                              </td>
                              <td className="px-3 py-2 text-slate-700 dark:text-slate-200">{game.sessionCount}</td>
                              <td className="px-3 py-2 font-semibold text-emerald-700 dark:text-emerald-200">
                                {formatHours(game.totalSeconds, 1)}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>
              </div>
            </>
          )}
        </div>
      </section>
    </AppLayout>
  );
}

function statRows(items: LabeledValue[]) {
  return items.map((item) => (
    <div key={item.label} className="flex items-center justify-between">
      <span className="text-[10px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">{item.label}</span>
      <span className="font-semibold text-slate-900 dark:text-white">{item.value}</span>
    </div>
  ));
}

function statCards(items: LabeledValue[]) {
  return items.map((item) => (
    <div key={item.label} className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/40 p-3">
      <p className="text-[10px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">{item.label}</p>
      <p className="mt-2 text-sm font-semibold text-slate-900 dark:text-white">{item.value}</p>
    </div>
  ));
}
