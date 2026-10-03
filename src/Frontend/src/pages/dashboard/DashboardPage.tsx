import { useCallback, useEffect, useRef, useState } from "react";
import { AppLayout } from "@/components/AppLayout";
import { StatsPieChart } from "@/components/StatsPieChart";
import { StatsRecapSection } from "@/components/StatsRecapSection";
import { apiRequest, errorMessage } from "@/lib/api";
import { encodeGameId } from "@/lib/gameHash";
import { logout, useAppNavigate } from "@/lib/navigation";
import { useTheme } from "@/lib/theme";
import type { ManualActiveSessionResponse, ManualSessionStopResponse } from "@/models/manual";
import type { AccountCurrentSessionResponse, AccountSessionsResponse } from "@/models/sessions";
import type { AccountStatsRangeResponse } from "@/models/stats";
import {
  beginRefresh,
  completeRefresh,
  dashboardRequests,
  failRefresh,
  getCurrentSession,
  getStatsView,
  getTodayPlatforms,
  initialDashboardState,
  recapPath,
  setTodaySessionsPage,
  todayHours,
  todaySessionPageItems,
  todaySessionsTotalPages,
  waitAllOrCancel,
  type DashboardState,
  type DashboardStatsView,
  type RecapRange,
} from "./dashboardState";

const lightPieColors: readonly string[] = ["#059669", "#0e7490", "#7c3aed", "#d97706"];
const darkPieColors: readonly string[] = ["#34d399", "#22d3ee", "#a78bfa", "#f59e0b"];
const monthNameFormat = new Intl.DateTimeFormat("en-US", { month: "long" });

const pagerButtonClass =
  "rounded-full border border-slate-200 dark:border-slate-800 px-3 py-1.5 text-slate-700 dark:text-slate-200 transition hover:border-emerald-400/60 hover:text-emerald-700 dark:hover:text-emerald-200 disabled:cursor-not-allowed disabled:opacity-50";

function summaryOf(view: DashboardStatsView) {
  return [
    { label: "Hours played", value: view.hours },
    { label: "Sessions", value: view.sessions },
    { label: "Top title", value: view.topTitle },
  ];
}

export function DashboardPage() {
  const { theme } = useTheme();
  const navigate = useAppNavigate();
  const [state, setState] = useState<DashboardState>(() => beginRefresh(initialDashboardState, false));
  const [isStoppingManualSession, setIsStoppingManualSession] = useState(false);
  const [manualActionError, setManualActionError] = useState("");
  const lifetime = useRef<AbortController>(new AbortController());
  const stoppingRef = useRef(false);

  const refreshDashboard = useCallback(async (silent: boolean) => {
    const lifetimeController = lifetime.current;
    setState((current) => beginRefresh(current, silent));
    const siblingCancellation = new AbortController();
    const linkAbort = () => siblingCancellation.abort();
    lifetimeController.signal.addEventListener("abort", linkAbort);
    try {
      const requests = dashboardRequests(new Date());
      const options = { onAuthFailure: logout, signal: siblingCancellation.signal };
      const [todaySessions, todayStats, monthlyStats, yearlyStats, currentSession, activeManualSession] =
        await waitAllOrCancel<
          [
            AccountSessionsResponse,
            AccountStatsRangeResponse,
            AccountStatsRangeResponse,
            AccountStatsRangeResponse,
            AccountCurrentSessionResponse,
            ManualActiveSessionResponse,
          ]
        >(
          [
            apiRequest<AccountSessionsResponse>(requests.todaySessions, options),
            apiRequest<AccountStatsRangeResponse>(requests.todayStats, options),
            apiRequest<AccountStatsRangeResponse>(requests.monthlyStats, options),
            apiRequest<AccountStatsRangeResponse>(requests.yearlyStats, options),
            apiRequest<AccountCurrentSessionResponse>(requests.currentSession, options),
            apiRequest<ManualActiveSessionResponse>(requests.activeManualSession, options),
          ],
          siblingCancellation,
        );
      if (lifetimeController.signal.aborted) return;

      setState((current) =>
        completeRefresh(
          current,
          {
            todaySessions: todaySessions.sessions,
            todayPlatformTotals: todayStats.stats.platforms,
            currentSession: currentSession.session,
            activeManualSession: activeManualSession.session,
            monthlyStats: monthlyStats.stats,
            yearlyStats: yearlyStats.stats,
          },
          new Date(),
        ),
      );
    } catch (error) {
      if (lifetimeController.signal.aborted) return;
      setState((current) => failRefresh(current, errorMessage(error)));
    } finally {
      lifetimeController.signal.removeEventListener("abort", linkAbort);
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    lifetime.current = controller;
    void refreshDashboard(false);
    return () => controller.abort();
  }, [refreshDashboard]);

  const stopManualSession = async () => {
    if (stoppingRef.current) return;
    const lifetimeController = lifetime.current;
    stoppingRef.current = true;
    setIsStoppingManualSession(true);
    setManualActionError("");
    try {
      await apiRequest<ManualSessionStopResponse>("/manual/sessions/stop", {
        method: "POST",
        signal: lifetimeController.signal,
      });
      await refreshDashboard(true);
    } catch (error) {
      if (!lifetimeController.signal.aborted) setManualActionError(errorMessage(error));
    } finally {
      stoppingRef.current = false;
      if (!lifetimeController.signal.aborted) setIsStoppingManualSession(false);
    }
  };

  const navigateToRecap = (range: RecapRange) => navigate(recapPath(range, new Date()));
  const navigateToGame = (gameId: string) => navigate(`/games/${encodeGameId(gameId)}`);

  const platformPieColors = theme === "dark" ? darkPieColors : lightPieColors;
  const now = new Date();
  const currentSession = getCurrentSession(state, now);
  const todaySessionCards = state.todaySessionCards;
  const todayPlatforms = getTodayPlatforms(state);
  const todayPieSegments = todayPlatforms.map((item) => ({ key: item.key, label: item.label, value: item.value }));
  const monthly = getStatsView(state.monthlyStats);
  const yearly = getStatsView(state.yearlyStats);
  const totalPages = todaySessionsTotalPages(state);
  const monthlyHighlightsLabel = `${monthNameFormat.format(now)} highlights`;
  const yearlyRecapLabel = `${now.getFullYear()} recap`;

  return (
    <AppLayout title="Dashboard">
      <section className="mt-10 space-y-6">
        <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 px-4 py-3 sm:px-6 sm:py-4">
          <div className="flex flex-wrap items-center justify-between gap-4">
            <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
              Current session
            </p>
            <div className="flex flex-wrap items-center gap-3">
              <span className="rounded-full border border-emerald-400/40 px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-200">
                {currentSession === null ? "Idle" : "Live"}
              </span>
            </div>
          </div>
          {state.isDashboardLoading ? (
            <div className="mt-3 flex flex-wrap items-center justify-between gap-5">
              <div className="h-6 w-40 rounded-full bg-slate-200/70 dark:bg-slate-800/60"></div>
              <div className="flex flex-wrap items-center justify-end gap-4">
                <div className="h-3 w-24 rounded-full bg-slate-200/70 dark:bg-slate-800/60"></div>
                <div className="h-3 w-32 rounded-full bg-slate-200/70 dark:bg-slate-800/60"></div>
                <div className="h-3 w-28 rounded-full bg-slate-200/70 dark:bg-slate-800/60"></div>
              </div>
            </div>
          ) : (
            <>
              <div className="mt-3 flex flex-wrap items-center justify-between gap-5 text-sm font-semibold text-slate-600 dark:text-slate-300">
                {currentSession !== null ? (
                  <>
                    <button
                      type="button"
                      onClick={() => navigateToGame(currentSession.gameId)}
                      className="text-left text-lg font-semibold text-slate-900 dark:text-white transition hover:text-emerald-700 dark:hover:text-emerald-200"
                    >
                      {currentSession.title}
                    </button>
                    <div className="flex flex-wrap items-center justify-end gap-4 text-sm font-semibold normal-case tracking-normal text-slate-600 dark:text-slate-300">
                      <span className="inline-flex items-center gap-2 text-slate-700 dark:text-slate-200">
                        <svg aria-hidden="true" viewBox="0 0 20 20" className="h-3.5 w-3.5 text-emerald-700 dark:text-emerald-300" fill="currentColor">
                          <path d="M3 5a2 2 0 012-2h10a2 2 0 012 2v2H3V5zm0 4h14v6a2 2 0 01-2 2H5a2 2 0 01-2-2V9z"></path>
                        </svg>
                        {currentSession.platform}
                      </span>
                      <span className="inline-flex items-center gap-2 text-slate-600 dark:text-slate-300">
                        <svg aria-hidden="true" viewBox="0 0 20 20" className="h-3.5 w-3.5 text-slate-500 dark:text-slate-400" fill="currentColor">
                          <path d="M10 2a8 8 0 100 16 8 8 0 000-16zm1 4v4.59l3.3 3.3-1.42 1.42L9 11V6h2z"></path>
                        </svg>
                        Started {currentSession.startedAt}
                      </span>
                      <span className="inline-flex items-center gap-2 text-emerald-700 dark:text-emerald-200">
                        <svg aria-hidden="true" viewBox="0 0 20 20" className="h-3.5 w-3.5 text-emerald-700 dark:text-emerald-300" fill="currentColor">
                          <path d="M10 2a8 8 0 100 16 8 8 0 000-16zm-1 3h2v6h4v2H9V5z"></path>
                        </svg>
                        Elapsed {currentSession.elapsed}
                      </span>
                      {currentSession.canStop && (
                        <button
                          type="button"
                          disabled={isStoppingManualSession}
                          onClick={() => void stopManualSession()}
                          className="rounded-full border border-rose-500/60 px-4 py-2 text-xs font-semibold uppercase tracking-[0.18em] text-rose-700 transition hover:border-rose-400 hover:bg-rose-50 disabled:cursor-not-allowed disabled:opacity-60 dark:text-rose-200 dark:hover:bg-rose-950/40"
                        >
                          {isStoppingManualSession ? "Stopping..." : "Stop session"}
                        </button>
                      )}
                    </div>
                  </>
                ) : (
                  <h2 className="text-sm font-semibold text-slate-700 dark:text-slate-200">No session running right now.</h2>
                )}
              </div>
              {manualActionError.length > 0 && (
                <p role="alert" className="mt-3 text-sm text-rose-700 dark:text-rose-300">
                  {manualActionError}
                </p>
              )}
            </>
          )}
        </div>

        <div className="grid gap-6 lg:grid-cols-[0.9fr_1.1fr]">
          <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                  Today stats
                </p>
                <h2 className="text-xl font-semibold text-slate-900 dark:text-white">{todayHours(state)} played</h2>
              </div>
              <button
                type="button"
                onClick={() => navigateToRecap("today")}
                className="flex h-9 w-9 items-center justify-center rounded-xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/60 text-slate-600 dark:text-slate-200 transition hover:border-emerald-300 hover:text-emerald-700 dark:hover:text-emerald-200"
                aria-label="Open recap"
                title="Open recap"
              >
                <svg
                  xmlns="http://www.w3.org/2000/svg"
                  width="24"
                  height="24"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  className="lucide lucide-chart-column h-4 w-4"
                  aria-hidden="true"
                >
                  <path d="M3 3v16a2 2 0 0 0 2 2h16"></path>
                  <path d="M18 17V9"></path>
                  <path d="M13 17V5"></path>
                  <path d="M8 17v-3"></path>
                </svg>
              </button>
            </div>
            <div className="mt-5 rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4">
              <p className="text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">Platform distribution</p>
              {todayPlatforms.length === 0 ? (
                <p className="mt-3 text-xs text-slate-500 dark:text-slate-400">No sessions logged today yet.</p>
              ) : (
                <div className="mt-4 flex flex-wrap items-center gap-4">
                  <StatsPieChart segments={todayPieSegments} colors={platformPieColors} size={140} />
                  <div className="flex-1 space-y-2 text-xs text-slate-600 dark:text-slate-300">
                    {todayPlatforms.map((item, index) => (
                      <div key={index} className="flex items-center justify-between gap-3">
                        <div className="flex items-center gap-2">
                          <span
                            className="h-2 w-2 rounded-full"
                            style={{ backgroundColor: platformPieColors[index % platformPieColors.length] }}
                          ></span>
                          <span>{item.label}</span>
                        </div>
                        <span>
                          {item.value}% · {item.hours}
                        </span>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </div>
          </div>

          <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                  Today sessions
                </p>
                <h2 className="text-xl font-semibold text-slate-900 dark:text-white">
                  {todaySessionCards.length} {todaySessionCards.length === 1 ? "session" : "sessions"} logged
                </h2>
              </div>
              <button
                type="button"
                onClick={() => navigate("/sessions")}
                className="rounded-full border border-slate-300 dark:border-slate-700 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-700 dark:text-slate-200 transition hover:border-emerald-400/60"
              >
                View all
              </button>
            </div>
            <div className="mt-5 space-y-4">
              {state.dashboardError.length > 0 ? (
                <div className="rounded-2xl border border-rose-500/30 bg-rose-500/10 p-4 text-xs text-rose-700 dark:text-rose-200">
                  {state.dashboardError}
                </div>
              ) : state.isDashboardLoading ? (
                [1, 2, 3].map((item) => (
                  <div
                    key={item}
                    className="flex items-center justify-between gap-4 rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4"
                  >
                    <div className="flex items-center gap-3">
                      <div className="h-14 w-10 rounded-xl bg-slate-200/70 dark:bg-slate-800/60"></div>
                      <div className="space-y-2">
                        <div className="h-3 w-28 rounded-full bg-slate-200/70 dark:bg-slate-800/70"></div>
                        <div className="h-2 w-16 rounded-full bg-slate-200/50 dark:bg-slate-800/50"></div>
                      </div>
                    </div>
                    <div className="h-3 w-12 rounded-full bg-slate-200/70 dark:bg-slate-800/60"></div>
                  </div>
                ))
              ) : todaySessionCards.length === 0 ? (
                <div className="rounded-2xl border border-dashed border-slate-200 dark:border-slate-800 bg-slate-200/40 dark:bg-slate-950/40 p-4 text-xs text-slate-500 dark:text-slate-400">
                  No sessions logged today yet.
                </div>
              ) : (
                <>
                  {todaySessionPageItems(state).map((session) => (
                    <div
                      key={session.sessionId}
                      className="relative flex flex-wrap items-center justify-between gap-4 overflow-hidden rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4"
                      style={
                        session.imageUrl === null
                          ? undefined
                          : {
                              backgroundImage: `url(${session.imageUrl})`,
                              backgroundSize: "cover",
                              backgroundPosition: "center",
                            }
                      }
                    >
                      <div
                        className={`absolute inset-0 ${session.imageUrl === null ? "bg-white/90 dark:bg-slate-900/70" : "bg-white/90 dark:bg-slate-950/80"}`}
                      ></div>
                      <div className="relative z-10 flex items-center gap-3">
                        {session.imageUrl === null && <div className="h-14 w-10 rounded-xl bg-slate-200/70 dark:bg-slate-800/60"></div>}
                        <button
                          type="button"
                          onClick={() => navigateToGame(session.gameId)}
                          className="text-left text-sm font-semibold text-slate-900 dark:text-white transition hover:text-emerald-700 dark:hover:text-emerald-200"
                        >
                          {session.title}
                        </button>
                      </div>
                      <div className="relative z-10 flex items-center gap-4">
                        <p className="text-xs text-slate-500 dark:text-slate-400">{session.platform}</p>
                        <p className="text-sm font-semibold text-emerald-700 dark:text-emerald-200">{session.duration}</p>
                      </div>
                    </div>
                  ))}

                  {totalPages > 1 && (
                    <div className="flex items-center justify-between pt-2 text-xs uppercase tracking-[0.2em] text-slate-500 dark:text-slate-400">
                      <button
                        type="button"
                        className={pagerButtonClass}
                        onClick={() => setState((current) => setTodaySessionsPage(current, current.todaySessionsPage - 1))}
                        disabled={state.todaySessionsPage === 1}
                      >
                        Prev
                      </button>
                      <span>
                        {state.todaySessionsPage} of {totalPages}
                      </span>
                      <button
                        type="button"
                        className={pagerButtonClass}
                        onClick={() => setState((current) => setTodaySessionsPage(current, current.todaySessionsPage + 1))}
                        disabled={state.todaySessionsPage === totalPages}
                      >
                        Next
                      </button>
                    </div>
                  )}
                </>
              )}
            </div>
          </div>
        </div>

        <StatsRecapSection
          title="Monthly stats"
          subtitle={monthlyHighlightsLabel}
          summary={summaryOf(monthly)}
          platforms={monthly.platforms}
          topGames={monthly.topGames}
          colors={platformPieColors}
          onGameSelect={navigateToGame}
          onOpenRecap={() => navigateToRecap("month")}
        />

        <StatsRecapSection
          title="Yearly stats"
          subtitle={yearlyRecapLabel}
          summary={summaryOf(yearly)}
          platforms={yearly.platforms}
          topGames={yearly.topGames}
          colors={platformPieColors}
          onGameSelect={navigateToGame}
          onOpenRecap={() => navigateToRecap("year")}
        />
      </section>
    </AppLayout>
  );
}
