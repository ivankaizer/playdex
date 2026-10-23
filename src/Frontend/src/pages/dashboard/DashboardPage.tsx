import { useCallback, useEffect, useRef, useState } from "react";
import { AppLayout } from "@/components/AppLayout";
import { AppLink } from "@/components/AppLink";
import { BarChart, Cover, Donut, Dot, ErrorNote, LegendRow, Segmented, ShareBar, StackedBar } from "@/components/ui";
import { apiRequest, errorMessage, isAbortError } from "@/lib/api";
import { formatCountNoun } from "@/lib/format";
import { encodeGameId } from "@/lib/gameHash";
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
  completeSection,
  dailyPlatforms,
  dailySessionCounts,
  dailyTotals,
  dashboardActivityRequests,
  dashboardRequests,
  failSection,
  formatHoursGrouped,
  formatShortDuration,
  getOverviewView,
  getRecentSessions,
  getTodayView,
  getWeekView,
  greetingFor,
  initialDashboardState,
  recapPath,
  recentSessionsRequest,
  type DashboardActivity,
  type DashboardPlatformView,
  type DashboardSection,
  type DashboardSectionData,
  type DashboardState,
  type OverviewKpi,
  type OverviewPeriod,
} from "./dashboardState";

const monthName = new Intl.DateTimeFormat("en-US", { month: "long" });

/** Long game titles step down in size so they stay readable in a KPI tile. */
function kpiTitleSize(value: string): string {
  const length = Array.from(value).length;
  return length <= 12 ? "clamp(24px,2.6vw,36px)" : length <= 24 ? "clamp(20px,2vw,28px)" : "clamp(17px,1.6vw,22px)";
}

/**
 * Overview KPI tile. Figures never truncate; they shrink with clamp(). The top title wraps to three lines
 * with the full name in `title`.
 */
function KpiTile({ kpi, loading, text }: { kpi: OverviewKpi; loading: boolean; text: boolean }) {
  const value = loading ? "—" : kpi.value;
  return (
    <div className="pdx-tile">
      <p className="pdx-label">{kpi.label}</p>
      {text ? (
        <p
          title={value}
          className="mt-2.5 line-clamp-3 font-semibold leading-[1.2] tracking-[-0.02em] [overflow-wrap:anywhere]"
          style={{ fontSize: kpiTitleSize(value) }}
        >
          {value}
        </p>
      ) : (
        <p className="pdx-num mt-2.5 whitespace-nowrap text-[clamp(26px,8vw,36px)] font-semibold leading-tight tracking-[-0.02em]">
          {value}
        </p>
      )}
      {!loading && kpi.sub !== undefined && <p className="mt-1 text-[13px] text-muted">{kpi.sub}</p>}
    </div>
  );
}

/** Two-column (auto-fit) platform legend with "52% · 64.9h" per row. */
function PlatformLegend({ items, className }: { items: readonly DashboardPlatformView[]; className?: string }) {
  return (
    <div className={className}>
      {items.map((item) => (
        <LegendRow
          key={item.key}
          color={platformColor(item.key, item.label)}
          name={<span title={item.label}>{item.label}</span>}
          pct={item.share}
          detail={item.hours}
        />
      ))}
    </div>
  );
}

/** Hides every bar label except the first of each quarter on phones (the year chart's minor months). */
function minorLabel(label: string, minor: boolean) {
  return minor ? <span className="max-[600px]:invisible">{label}</span> : label;
}

export function DashboardPage() {
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
      const daily = await apiRequest<AccountDailyStatsResponse>(requests.daily, { signal });
      if (signal.aborted || version !== activityVersion.current) return;
      setActivity({
        days: dailyTotals(daily.days),
        sessionCounts: dailySessionCounts(daily.days),
        dayPlatforms: dailyPlatforms(daily.days),
      });
    } catch (error) {
      if (signal.aborted || isAbortError(error) || version !== activityVersion.current) return;
      setActivity(null);
    }
  }, []);

  /** Each panel loads on its own: a failing endpoint leaves its error in that panel, not a blank dashboard. */
  const refreshDashboard = useCallback(
    async (silent: boolean) => {
      const lifetimeController = lifetime.current;
      const signal = lifetimeController.signal;
      setState((current) => beginRefresh(current, silent));
      void refreshActivity(signal);
      const requests = dashboardRequests(new Date());
      const options = { signal };
      const load = async <S extends DashboardSection>(section: S, fetch: () => Promise<DashboardSectionData[S]>) => {
        try {
          const data = await fetch();
          if (!signal.aborted) setState((current) => completeSection(current, section, data, new Date()));
        } catch (error) {
          if (signal.aborted || isAbortError(error)) return;
          setState((current) => failSection(current, section, errorMessage(error)));
        }
      };
      await Promise.all([
        load("live", async () => {
          const [current, manual] = await Promise.all([
            apiRequest<AccountCurrentSessionResponse>(requests.currentSession, options),
            apiRequest<ManualActiveSessionResponse>(requests.activeManualSession, options),
          ]);
          return { currentSession: current.session, activeManualSession: manual.session };
        }),
        load("today", async () => {
          const [sessions, stats] = await Promise.all([
            apiRequest<AccountSessionsResponse>(requests.todaySessions, options),
            apiRequest<AccountStatsRangeResponse>(requests.todayStats, options),
          ]);
          return {
            todaySessions: sessions.sessions,
            todaySessionCount: stats.stats.sessionCount,
            todayPlatformTotals: stats.stats.platforms,
          };
        }),
        load("recent", async () => ({
          recentSessions: (await apiRequest<AccountSessionsResponse>(recentSessionsRequest, options)).sessions,
        })),
        load("month", async () => ({
          monthlyStats: (await apiRequest<AccountStatsRangeResponse>(requests.monthlyStats, options)).stats,
        })),
        load("year", async () => ({
          yearlyStats: (await apiRequest<AccountStatsRangeResponse>(requests.yearlyStats, options)).stats,
        })),
      ]);
    },
    [refreshActivity],
  );

  useEffect(() => {
    const controller = new AbortController();
    lifetime.current = controller;
    void refreshDashboard(false);
    apiRequest<ManualPlatformsResponse>("/manual/platforms", { signal: controller.signal })
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
    runLiveAction((signal) => apiRequest<ManualSessionStartResponse>("/manual/sessions/start", { method: "POST", body: request, signal }));

  const searchLiveGames = useCallback((path: string, signal: AbortSignal) => apiRequest<ManualMappingSearchResponse>(path, { signal }), []);

  const gamePath = (gameId: string) => `/games/${encodeGameId(gameId)}`;

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
  const { loading, errors } = state;
  // Today falls back to the daily breakdown, which loads on its own: the today panel's error matters only without it.
  const todayError = activity === null ? errors.today : "";
  const today = getTodayView(state, activity, now);
  const week = getWeekView(activity, now);
  const recent = getRecentSessions(state, now);
  const overview = getOverviewView(state, activity, period, now);
  const overviewBars =
    period === "year" ? overview.bars.map((bar, index) => ({ ...bar, label: minorLabel(bar.label, index % 3 !== 0) })) : overview.bars;

  return (
    <AppLayout title={greetingFor(now)}>
      {loading.live ? (
        <section
          aria-label="Live session controls"
          aria-busy="true"
          className="pdx-card !rounded-[28px] !py-3 !pl-7 !pr-3 min-[1041px]:!rounded-full"
        >
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
          gamePath={gamePath}
          searchGames={searchLiveGames}
          onStart={startManualSession}
          onStop={stopManualSession}
        />
      )}

      {errors.live.length > 0 && <ErrorNote>Couldn't check for a running session. {errors.live}</ErrorNote>}

      <section className="pdx-card grid gap-x-14 gap-y-10 [grid-template-columns:repeat(auto-fit,minmax(min(340px,100%),1fr))]">
        <div className="flex min-w-0 flex-col gap-8">
          <div>
            <div className="flex items-center justify-between gap-3">
              <h2 className="pdx-kicker">Today</h2>
              <AppLink to={recapPath("today", now)} className="pdx-icon-btn !h-8 !w-8" aria-label="Open recap" title="Open recap">
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
                  <path d="M3 3v16a2 2 0 0 0 2 2h16"></path>
                  <path d="M18 17V9"></path>
                  <path d="M13 17V5"></path>
                  <path d="M8 17v-3"></path>
                </svg>
              </AppLink>
            </div>
            {todayError.length > 0 ? (
              <div className="mt-3">
                <ErrorNote>Couldn't load today's play time. {todayError}</ErrorNote>
              </div>
            ) : (
              <div className="mt-3 flex flex-wrap items-baseline gap-3">
                <span className="pdx-num text-[64px] font-semibold leading-none tracking-[-0.03em]">
                  {loading.today ? "—" : formatShortDuration(today.seconds)}
                </span>
                {!loading.today && (
                  <span className="text-[15px] text-muted">played across {formatCountNoun(today.sessions, "session")}</span>
                )}
              </div>
            )}
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
              {activity !== null && <span className="text-[13px] text-muted">{formatCountNoun(week.platforms.length, "platform")}</span>}
            </div>
            <StackedBar
              segments={week.platforms.map((item) => ({
                key: item.key,
                label: item.label,
                value: item.seconds,
                color: platformColor(item.key, item.label),
                detail: item.hours,
              }))}
            />
            {week.platforms.length === 0 ? (
              <p className="text-sm text-muted">No play this week yet.</p>
            ) : (
              <PlatformLegend
                items={week.platforms}
                className="grid gap-x-6 gap-y-2.5 [grid-template-columns:repeat(auto-fit,minmax(min(170px,100%),1fr))]"
              />
            )}
          </div>
        </div>

        <div className="flex min-w-0 flex-col gap-2">
          <div className="mb-2 flex items-center justify-between gap-3">
            <h2 className="pdx-kicker">Recent sessions</h2>
            <AppLink to="/sessions" className="pdx-btn-outline !h-8 !px-3.5 !text-xs !tracking-[0.06em] no-underline">
              View all
            </AppLink>
          </div>
          {errors.recent.length > 0 ? (
            <ErrorNote>Couldn't load your recent sessions. {errors.recent}</ErrorNote>
          ) : loading.recent ? (
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
            <p className="pdx-dashed">No sessions yet. Start one above, or log a past session from Sessions.</p>
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
                  <AppLink
                    to={gamePath(session.gameId)}
                    title={session.title}
                    className="block max-w-full truncate text-[15px] font-medium no-underline transition-colors hover:text-accent"
                  >
                    {session.title}
                  </AppLink>
                  <p className="mt-0.5 flex items-center gap-1.5 text-[13px] text-muted">
                    <Dot color={platformColor(session.platformKey, session.platform)} size={7} />
                    <span className="min-w-0 truncate">
                      {session.platform} · {session.date}
                    </span>
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
            <AppLink to={recapPath(period, now)} className="pdx-btn-outline !h-[38px] !text-[13px] no-underline">
              Open recap
            </AppLink>
            <Segmented<OverviewPeriod>
              ariaLabel="Overview period"
              value={period}
              onChange={setPeriod}
              options={[
                { value: "month", label: monthName.format(now) },
                { value: "year", label: String(now.getFullYear()) },
              ]}
            />
          </div>
        </div>

        {errors[period].length > 0 && (
          <ErrorNote>
            Couldn't load {period === "month" ? "this month's" : "this year's"} stats. {errors[period]}
          </ErrorNote>
        )}

        <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(200px,100%),1fr))]">
          {overview.kpis.map((kpi) => (
            <KpiTile key={kpi.label} kpi={kpi} loading={loading[period]} text={kpi.label === "Top title"} />
          ))}
        </div>

        <div className="flex flex-col gap-3.5">
          <div className="flex flex-wrap items-baseline justify-between gap-3">
            <span className="pdx-label">{overview.activityLabel}</span>
            {overview.activityNote && <span className="pdx-num text-[13px] text-muted">{overview.activityNote}</span>}
          </div>
          <BarChart
            data={overviewBars}
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
                  detail: item.hours,
                }))}
                center={
                  <div>
                    <div className="pdx-num text-xl font-semibold">{formatHoursGrouped(overview.totalSeconds)}</div>
                    <div className="text-xs text-muted">total</div>
                  </div>
                }
              />
              {overview.platforms.length === 0 ? (
                <p className="min-w-0 flex-[1_1_140px] text-sm text-muted">No play yet. Platforms show up after your first session.</p>
              ) : (
                <PlatformLegend items={overview.platforms} className="flex min-w-0 flex-[1_1_140px] flex-col gap-3.5" />
              )}
            </div>
          </div>

          <div className="pdx-tile flex flex-col gap-[18px] !p-7">
            <p className="pdx-label">Top games</p>
            {overview.topGames.map((game, index) => (
              <div key={game.gameId} className="grid grid-cols-[26px_minmax(0,1fr)_auto] items-baseline gap-x-3 gap-y-1.5">
                <span className="pdx-num text-[13px] text-muted">{String(index + 1).padStart(2, "0")}</span>
                <AppLink
                  to={gamePath(game.gameId)}
                  title={game.title}
                  className="truncate text-[15px] font-medium no-underline transition-colors hover:text-accent"
                >
                  {game.title}
                </AppLink>
                <span className="pdx-num text-sm font-semibold text-accent">{game.hours}</span>
                <ShareBar pct={game.pct} className="col-start-2 col-end-4" />
              </div>
            ))}
            {!loading[period] && overview.fewGamesNote && <p className="pdx-dashed mt-auto">{overview.fewGamesNote}</p>}
          </div>
        </div>
      </section>
    </AppLayout>
  );
}
