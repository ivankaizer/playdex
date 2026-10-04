import { useCallback, useEffect, useRef, useState } from "react";
import { AppLayout } from "@/components/AppLayout";
import {
  BarChart,
  Cover,
  Donut,
  Dot,
  ErrorNote,
  LegendRow,
  Segmented,
  ShareBar,
  StackedBar,
  StatTile,
} from "@/components/ui";
import { apiRequest, errorMessage, isAbortError } from "@/lib/api";
import { encodeGameId } from "@/lib/gameHash";
import { logout, useAppNavigate } from "@/lib/navigation";
import { platformColor } from "@/lib/platforms";
import type {
  ManualActiveSession,
  ManualActiveSessionResponse,
  ManualMappingSearchResponse,
  ManualPlatform,
  ManualPlatformsResponse,
  ManualSessionStartRequest,
  ManualSessionStartResponse,
  ManualSessionStopResponse,
} from "@/models/manual";
import type { AccountCurrentSessionResponse, AccountSessionsResponse } from "@/models/sessions";
import type { AccountDailyStatsResponse, AccountStatsRangeResponse } from "@/models/stats";
import { LiveSessionControl } from "../sessions/components/LiveSessionControl";
import {
  beginRefresh,
  completeRefresh,
  dailyTotals,
  dashboardActivityRequests,
  dashboardRequests,
  failRefresh,
  formatHoursGrouped,
  formatShortDuration,
  getOverviewView,
  getRecentSessions,
  getWeekView,
  initialDashboardState,
  recapPath,
  recentSessionsRequest,
  todaySeconds,
  waitAllOrCancel,
  type DashboardActivity,
  type DashboardState,
  type OverviewPeriod,
  type RecapRange,
} from "./dashboardState";

const utcMonthName = new Intl.DateTimeFormat("en-US", { month: "long", timeZone: "UTC" });

export function DashboardPage() {
  const navigate = useAppNavigate();
  const [state, setState] = useState<DashboardState>(() => beginRefresh(initialDashboardState, false));
  const [activity, setActivity] = useState<DashboardActivity | null>(null);
  const [platforms, setPlatforms] = useState<readonly ManualPlatform[]>([]);
  const [period, setPeriod] = useState<OverviewPeriod>("month");
  const [isLiveBusy, setIsLiveBusy] = useState(false);
  const [manualActionError, setManualActionError] = useState("");
  const lifetime = useRef<AbortController>(new AbortController());
  const busyRef = useRef(false);

  /** Per-day and weekly totals for the charts; failures only leave the charts empty. */
  const activityVersion = useRef(0);
  const refreshActivity = useCallback(async (signal: AbortSignal) => {
    const version = ++activityVersion.current;
    const requests = dashboardActivityRequests(new Date());
    try {
      const [week, daily] = await Promise.all([
        apiRequest<AccountStatsRangeResponse>(requests.week, { onAuthFailure: logout, signal }),
        apiRequest<AccountDailyStatsResponse>(requests.daily, { onAuthFailure: logout, signal }),
      ]);
      if (signal.aborted || version !== activityVersion.current) return;
      const totals = dailyTotals(daily.days);
      setActivity({ weekStats: week.stats, days: totals });
    } catch (error) {
      if (signal.aborted || isAbortError(error) || version !== activityVersion.current) return;
      setActivity(null);
    }
  }, []);

  const refreshDashboard = useCallback(
    async (silent: boolean) => {
      const lifetimeController = lifetime.current;
      setState((current) => beginRefresh(current, silent));
      void refreshActivity(lifetimeController.signal);
      const siblingCancellation = new AbortController();
      const linkAbort = () => siblingCancellation.abort();
      lifetimeController.signal.addEventListener("abort", linkAbort);
      try {
        const requests = dashboardRequests(new Date());
        const options = { onAuthFailure: logout, signal: siblingCancellation.signal };
        const [todaySessions, todayStats, monthlyStats, yearlyStats, currentSession, activeManualSession, recentSessions] =
          await waitAllOrCancel<
            [
              AccountSessionsResponse,
              AccountStatsRangeResponse,
              AccountStatsRangeResponse,
              AccountStatsRangeResponse,
              AccountCurrentSessionResponse,
              ManualActiveSessionResponse,
              AccountSessionsResponse,
            ]
          >(
            [
              apiRequest<AccountSessionsResponse>(requests.todaySessions, options),
              apiRequest<AccountStatsRangeResponse>(requests.todayStats, options),
              apiRequest<AccountStatsRangeResponse>(requests.monthlyStats, options),
              apiRequest<AccountStatsRangeResponse>(requests.yearlyStats, options),
              apiRequest<AccountCurrentSessionResponse>(requests.currentSession, options),
              apiRequest<ManualActiveSessionResponse>(requests.activeManualSession, options),
              apiRequest<AccountSessionsResponse>(recentSessionsRequest, options),
            ],
            siblingCancellation,
          );
        if (lifetimeController.signal.aborted) return;

        setState((current) =>
          completeRefresh(
            current,
            {
              todaySessions: todaySessions.sessions,
              todaySessionCount: todayStats.stats.sessionCount,
              recentSessions: recentSessions.sessions,
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
    },
    [refreshActivity],
  );

  useEffect(() => {
    const controller = new AbortController();
    lifetime.current = controller;
    void refreshDashboard(false);
    apiRequest<ManualPlatformsResponse>("/manual/platforms", { onAuthFailure: logout, signal: controller.signal })
      .then((response) => {
        if (!controller.signal.aborted) setPlatforms(response.platforms);
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) setManualActionError(errorMessage(error));
      });
    return () => controller.abort();
  }, [refreshDashboard]);

  const runLiveAction = async (action: (signal: AbortSignal) => Promise<unknown>) => {
    if (busyRef.current) return;
    const lifetimeController = lifetime.current;
    busyRef.current = true;
    setIsLiveBusy(true);
    setManualActionError("");
    try {
      await action(lifetimeController.signal);
      await refreshDashboard(true);
    } catch (error) {
      if (!lifetimeController.signal.aborted) setManualActionError(errorMessage(error));
    } finally {
      busyRef.current = false;
      if (!lifetimeController.signal.aborted) setIsLiveBusy(false);
    }
  };

  const stopManualSession = () =>
    runLiveAction((signal) => apiRequest<ManualSessionStopResponse>("/manual/sessions/stop", { method: "POST", signal }));

  const startManualSession = (request: ManualSessionStartRequest) =>
    runLiveAction((signal) =>
      apiRequest<ManualSessionStartResponse>("/manual/sessions/start", { method: "POST", body: request, signal }),
    );

  const searchLiveGames = useCallback(
    (path: string, signal: AbortSignal) => apiRequest<ManualMappingSearchResponse>(path, { onAuthFailure: logout, signal }),
    [],
  );

  const navigateToRecap = (range: RecapRange) => navigate(recapPath(range, new Date()));
  const navigateToGame = (gameId: string) => navigate(`/games/${encodeGameId(gameId)}`);

  const now = new Date();
  const liveSession: ManualActiveSession | null =
    state.activeManualSession ??
    (state.currentSession === null
      ? null
      : {
          sessionId: state.currentSession.sessionId,
          gameId: state.currentSession.gameId,
          title: state.currentSession.game,
          platformKey: state.currentSession.platformKey,
          platformName: state.currentSession.platformName,
          startedAt: state.currentSession.startedAt,
          capsuleUrl: state.currentSession.capsuleUrl,
        });
  const loading = state.isDashboardLoading;
  const todayTotal = todaySeconds(state);
  const week = getWeekView(activity, now);
  const recent = getRecentSessions(state, now);
  const overview = getOverviewView(state, activity, period, now);
  const overviewStats = period === "month" ? state.monthlyStats : state.yearlyStats;
  const gameCount = overview.topGames.length;

  return (
    <AppLayout title="Dashboard">
      {loading ? (
        <section aria-label="Live session controls" aria-busy="true" className="pdx-card !rounded-[28px] !py-3 !pl-7 !pr-3 md:!rounded-full">
          <div className="flex h-10 items-center gap-3">
            <span aria-hidden="true" className="h-[7px] w-[7px] rounded-full bg-faint-2"></span>
            <span className="h-3 w-40 rounded-full bg-track"></span>
          </div>
        </section>
      ) : (
        <LiveSessionControl
          platforms={platforms}
          activeSession={liveSession}
          canStop={state.activeManualSession !== null}
          busy={isLiveBusy}
          error={manualActionError}
          onOpenGame={navigateToGame}
          searchGames={searchLiveGames}
          onStart={startManualSession}
          onStop={stopManualSession}
        />
      )}

      {state.dashboardError.length > 0 && <ErrorNote>{state.dashboardError}</ErrorNote>}

      <section className="pdx-card grid gap-x-14 gap-y-10 [grid-template-columns:repeat(auto-fit,minmax(min(340px,100%),1fr))]">
        <div className="flex min-w-0 flex-col gap-8">
          <div>
            <div className="flex items-center justify-between gap-3">
              <h2 className="pdx-kicker">Today</h2>
              <button
                type="button"
                onClick={() => navigateToRecap("today")}
                className="pdx-icon-btn !h-8 !w-8"
                aria-label="Open recap"
                title="Open recap"
              >
                <svg aria-hidden="true" viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M3 3v16a2 2 0 0 0 2 2h16"></path>
                  <path d="M18 17V9"></path>
                  <path d="M13 17V5"></path>
                  <path d="M8 17v-3"></path>
                </svg>
              </button>
            </div>
            <div className="mt-3 flex flex-wrap items-baseline gap-3">
              <span className="pdx-num text-[64px] font-semibold leading-none tracking-[-0.03em]">
                {loading ? "—" : formatShortDuration(todayTotal)}
              </span>
              {!loading && (
                <span className="text-[15px] text-muted">
                  played across {state.todaySessionCount} {state.todaySessionCount === 1 ? "session" : "sessions"}
                </span>
              )}
            </div>
          </div>

          <div className="flex flex-col gap-3">
            <div className="flex flex-wrap items-baseline justify-between gap-x-3">
              <span className="pdx-label">This week</span>
              <span className="pdx-num text-[13px] text-muted">
                {activity !== null && `${formatHoursGrouped(week.totalSeconds)} · `}
                {week.rangeLabel}
              </span>
            </div>
            <BarChart data={week.bars} height={120} gap={10} radius={10} minBar={6} ariaLabel="Playtime this week" />
          </div>

          <div className="flex flex-col gap-3.5">
            <div className="flex flex-wrap items-baseline justify-between gap-x-3">
              <span className="pdx-label">Platforms this week</span>
              {week.platforms.length > 0 && (
                <span className="text-[13px] text-muted">
                  {week.platforms.length} {week.platforms.length === 1 ? "platform" : "platforms"}
                </span>
              )}
            </div>
            {week.platforms.length === 0 ? (
              <p className="pdx-dashed">No play logged this week yet.</p>
            ) : (
              <>
                <StackedBar
                  segments={week.platforms.map((item) => ({
                    key: item.key,
                    label: item.label,
                    value: item.seconds,
                    color: platformColor(item.key, item.label),
                  }))}
                />
                <div className="grid grid-cols-2 gap-x-6 gap-y-2.5 max-[420px]:grid-cols-1">
                  {week.platforms.map((item) => (
                    <LegendRow
                      key={item.key}
                      color={platformColor(item.key, item.label)}
                      name={item.label}
                      pct={`${item.value}%`}
                      detail={item.hours}
                    />
                  ))}
                </div>
              </>
            )}
          </div>
        </div>

        <div className="flex min-w-0 flex-col gap-2">
          <div className="mb-2 flex items-center justify-between gap-3">
            <h2 className="pdx-kicker">Recent sessions</h2>
            <button
              type="button"
              onClick={() => navigate("/sessions")}
              className="pdx-btn-outline !h-8 !px-3.5 !text-xs !tracking-[0.06em]"
            >
              View all
            </button>
          </div>
          {loading ? (
            [1, 2, 3].map((item) => (
              <div key={item} className="grid grid-cols-[48px_minmax(0,1fr)_auto] items-center gap-4 py-2.5">
                <div className="h-12 w-12 rounded-[14px] bg-track"></div>
                <div className="space-y-2">
                  <div className="h-3 w-32 rounded-full bg-track"></div>
                  <div className="h-2 w-20 rounded-full bg-track/70"></div>
                </div>
                <div className="h-3 w-12 rounded-full bg-track"></div>
              </div>
            ))
          ) : recent.length === 0 ? (
            <p className="pdx-dashed">No sessions logged yet.</p>
          ) : (
            recent.map((session) => (
              <div
                key={session.sessionId}
                className="pdx-row -mx-3 grid grid-cols-[48px_minmax(0,1fr)_auto] items-center gap-4 px-3 py-2.5"
              >
                <Cover
                  title={session.title}
                  src={session.imageUrl}
                  className="h-12 w-12 rounded-[14px] shadow-[0_4px_10px_-4px_rgba(30,50,70,.35)]"
                />
                <div className="min-w-0">
                  <button
                    type="button"
                    onClick={() => navigateToGame(session.gameId)}
                    className="block max-w-full truncate text-left text-[15px] font-medium transition-colors hover:text-accent"
                  >
                    {session.title}
                  </button>
                  <p className="mt-0.5 flex items-center gap-1.5 truncate text-[13px] text-muted">
                    <Dot color={platformColor(session.platformKey, session.platform)} size={7} />
                    {session.platform} · {session.date}
                  </p>
                </div>
                <span className="pdx-num text-[15px] font-semibold text-accent">{session.duration}</span>
              </div>
            ))
          )}
        </div>
      </section>

      <section className="pdx-card flex flex-col gap-9">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div className="min-w-0">
            <p className="pdx-kicker">Overview</p>
            <h2 className="pdx-title">{overview.title}</h2>
          </div>
          <div className="flex flex-wrap items-center gap-3">
            <button type="button" onClick={() => navigateToRecap(period)} className="pdx-btn-outline !h-[38px] !text-[13px]">
              Open recap
            </button>
            <Segmented<OverviewPeriod>
              ariaLabel="Overview period"
              value={period}
              onChange={setPeriod}
              options={[
                { value: "month", label: utcMonthName.format(now) },
                { value: "year", label: String(now.getUTCFullYear()) },
              ]}
            />
          </div>
        </div>

        <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(200px,100%),1fr))]">
          {overview.kpis.map((kpi) => (
            <StatTile key={kpi.label} label={kpi.label} value={loading ? "—" : kpi.value} sub={loading ? undefined : kpi.sub} />
          ))}
        </div>

        <div className="flex flex-col gap-3.5">
          <div className="flex flex-wrap items-baseline justify-between gap-3">
            <span className="pdx-label">{overview.activityLabel}</span>
            {overview.activityNote && <span className="pdx-num text-[13px] text-muted">{overview.activityNote}</span>}
          </div>
          <BarChart
            data={overview.bars}
            height={140}
            gap={period === "month" ? 4 : 6}
            radius={period === "month" ? 6 : 8}
            minBar={6}
            ariaLabel={period === "month" ? "Daily playtime this month" : "Monthly playtime this year"}
          />
        </div>

        <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(320px,100%),1fr))]">
          <div className="pdx-tile flex flex-col gap-6 !p-7">
            <p className="pdx-label">Platforms</p>
            <div className="flex flex-wrap items-center gap-8">
              <Donut
                size={168}
                thickness={20}
                segments={overview.platforms.map((item) => ({
                  key: item.key,
                  label: item.label,
                  value: item.seconds,
                  color: platformColor(item.key, item.label),
                }))}
                center={
                  <div>
                    <div className="pdx-num text-xl font-semibold">{formatHoursGrouped(overview.totalSeconds)}</div>
                    <div className="text-xs text-muted">total</div>
                  </div>
                }
              />
              <div className="flex min-w-0 flex-[1_1_140px] flex-col gap-3.5">
                {overview.platforms.length === 0 ? (
                  <p className="text-sm text-muted">No platforms yet.</p>
                ) : (
                  overview.platforms.map((item) => (
                    <LegendRow
                      key={item.key}
                      color={platformColor(item.key, item.label)}
                      name={item.label}
                      pct={`${item.value}%`}
                      detail={item.hours}
                    />
                  ))
                )}
              </div>
            </div>
          </div>

          <div className="pdx-tile flex flex-col gap-[18px] !p-7">
            <p className="pdx-label">Top games</p>
            {overview.topGames.map((game, index) => (
              <div key={game.gameId} className="grid grid-cols-[26px_minmax(0,1fr)_auto] items-baseline gap-x-3 gap-y-1.5">
                <span className="pdx-num text-[13px] text-faint">{String(index + 1).padStart(2, "0")}</span>
                <button
                  type="button"
                  onClick={() => navigateToGame(game.gameId)}
                  className="truncate text-left text-[15px] font-medium transition-colors hover:text-accent"
                >
                  {game.title}
                </button>
                <span className="pdx-num text-sm font-semibold text-accent">{game.hours}</span>
                <ShareBar pct={game.pct} className="col-start-2 col-end-4" />
              </div>
            ))}
            {!loading && overviewStats !== null && gameCount < 3 && (
              <p className="pdx-dashed mt-auto">
                {gameCount === 0
                  ? `No games played yet this ${period}.`
                  : `Only ${gameCount === 1 ? "one game" : `${gameCount} games`} so far this ${period}. Your ranking fills in as you play.`}
              </p>
            )}
          </div>
        </div>
      </section>
    </AppLayout>
  );
}
