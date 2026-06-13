import { useEffect, useMemo, useState } from "react";

import { apiRequest } from "../api";
import { buildGamePath, navItems as defaultNavItems } from "../constants";
import type {
  AccountCurrentSessionResponse,
  AccountSessionsResponse,
  AccountStatsPeriod,
  AccountStatsRangeResponse,
  AccountSession,
  ManualInputsResponse,
  ManualInput,
  ManualSessionStartResponse,
  ManualSessionStopResponse,
  NavItem,
} from "../types";
import {
  formatDuration,
  formatHours,
  formatSessionPlatformLabel,
  formatStatsPlatformLabel,
  formatTimeLabel,
  formatUtcDateInput,
} from "../utils/formatters";
import { getSessionDurationSeconds } from "../utils/sessions";
import { AppFooter } from "../components/AppFooter";
import { AppHeader } from "../components/AppHeader";
import { Background } from "../components/Background";
import { ManualSessionModal } from "../components/ManualSessionModal";
import { StatsPieChart } from "../components/StatsPieChart";
import { StatsRecapSection } from "../components/StatsRecapSection";
import { BarChart3 } from "lucide-react";

type DashboardPageProps = {
  path: string;
  navItems?: NavItem[];
  onNavigate: (path: string) => void;
  onLogout: () => void;
  theme: "light" | "dark";
  onToggleTheme: () => void;
};

export const DashboardPage = ({
  path,
  navItems = defaultNavItems,
  onNavigate,
  onLogout,
  theme,
  onToggleTheme,
}: DashboardPageProps) => {
  const [todaySessions, setTodaySessions] = useState<AccountSession[]>([]);
  const [todayPlatformTotals, setTodayPlatformTotals] = useState<
    { platform: string; totalSeconds: number; source?: string | null }[]
  >([]);
  const [currentSession, setCurrentSession] = useState<AccountSession | null>(
    null
  );
  const [monthlyStats, setMonthlyStats] = useState<AccountStatsPeriod | null>(
    null
  );
  const [yearlyStats, setYearlyStats] = useState<AccountStatsPeriod | null>(
    null
  );
  const [isDashboardLoading, setIsDashboardLoading] = useState(false);
  const [dashboardError, setDashboardError] = useState("");
  const [manualInputs, setManualInputs] = useState<ManualInput[]>([]);
  const [isManualInputsLoading, setIsManualInputsLoading] = useState(false);
  const [manualInputsError, setManualInputsError] = useState("");
  const [isManualModalOpen, setIsManualModalOpen] = useState(false);
  const [manualInputSelection, setManualInputSelection] = useState("");
  const [manualInputText, setManualInputText] = useState("");
  const [manualStartError, setManualStartError] = useState("");
  const [isManualStartLoading, setIsManualStartLoading] = useState(false);
  const [isManualStopLoading, setIsManualStopLoading] = useState(false);

  const refreshDashboard = async (options?: {
    silent?: boolean;
    isCancelled?: () => boolean;
  }) => {
    if (!options?.silent) {
      setIsDashboardLoading(true);
    }
    setDashboardError("");

    try {
      const now = new Date();
      const todayDate = formatUtcDateInput(now);
      const monthStart = formatUtcDateInput(
        new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1))
      );
      const yearStart = formatUtcDateInput(
        new Date(Date.UTC(now.getUTCFullYear(), 0, 1))
      );
      const todaySessionsParams = new URLSearchParams({
        limit: "10",
        offset: "0",
        from: todayDate,
        to: todayDate,
        sort: "date",
        direction: "desc",
      });
      const statsParams = (from: string, to: string) =>
        new URLSearchParams({ from, to }).toString();
      const [
        todaySessionsResponse,
        todayStatsResponse,
        monthlyStatsResponse,
        yearlyStatsResponse,
        current,
      ] = await Promise.all([
        apiRequest<AccountSessionsResponse>(
          `/sessions?${todaySessionsParams.toString()}`,
          { onAuthFailure: onLogout }
        ),
        apiRequest<AccountStatsRangeResponse>(
          `/stats?${statsParams(todayDate, todayDate)}`,
          { onAuthFailure: onLogout }
        ),
        apiRequest<AccountStatsRangeResponse>(
          `/stats?${statsParams(monthStart, todayDate)}`,
          { onAuthFailure: onLogout }
        ),
        apiRequest<AccountStatsRangeResponse>(
          `/stats?${statsParams(yearStart, todayDate)}`,
          { onAuthFailure: onLogout }
        ),
        apiRequest<AccountCurrentSessionResponse>("/sessions/current", {
          onAuthFailure: onLogout,
        }),
      ]);

      if (options?.isCancelled?.()) {
        return;
      }

      setTodaySessions(todaySessionsResponse.sessions);
      setMonthlyStats(monthlyStatsResponse.stats);
      setYearlyStats(yearlyStatsResponse.stats);
      setCurrentSession(current.session);
      setTodayPlatformTotals(todayStatsResponse.stats.platforms);
      setIsDashboardLoading(false);
    } catch (err) {
      if (options?.isCancelled?.()) {
        return;
      }

      setTodaySessions([]);
      setMonthlyStats(null);
      setYearlyStats(null);
      setCurrentSession(null);
      setTodayPlatformTotals([]);
      setIsDashboardLoading(false);
      setDashboardError(
        err instanceof Error ? err.message : "Failed to load dashboard data."
      );
    }
  };

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

  const handleManualSessionStart = async () => {
    const title = manualInputText.trim();
    if (!title) {
      setManualStartError("Enter a title to start a session.");
      return;
    }

    setIsManualStartLoading(true);
    setManualStartError("");
    try {
      await apiRequest<ManualSessionStartResponse>("/manual/sessions/start", {
        method: "POST",
        body: { title },
      });
      closeManualModal();
      await loadManualInputs();
      await refreshDashboard({ silent: true });
    } catch (err) {
      setManualStartError(
        err instanceof Error ? err.message : "Failed to start session."
      );
    } finally {
      setIsManualStartLoading(false);
    }
  };

  const handleManualSessionStop = async () => {
    setIsManualStopLoading(true);
    setDashboardError("");
    try {
      await apiRequest<ManualSessionStopResponse>("/manual/sessions/stop", {
        method: "POST",
      });
      await refreshDashboard({ silent: true });
    } catch (err) {
      setDashboardError(
        err instanceof Error ? err.message : "Failed to stop session."
      );
    } finally {
      setIsManualStopLoading(false);
    }
  };

  const closeManualModal = () => {
    setIsManualModalOpen(false);
    setManualInputSelection("");
    setManualInputText("");
    setManualStartError("");
  };

  useEffect(() => {
    let cancelled = false;
    void refreshDashboard({ isCancelled: () => cancelled });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!isManualModalOpen) {
      return;
    }

    void loadManualInputs();
  }, [isManualModalOpen]);

  const platformPieColors =
    theme === "dark"
      ? ["#34d399", "#22d3ee", "#a78bfa", "#f59e0b"]
      : ["#059669", "#0e7490", "#7c3aed", "#d97706"];
  const recapNow = new Date();
  const recapToday = formatUtcDateInput(recapNow);
  const recapMonthStart = formatUtcDateInput(
    new Date(Date.UTC(recapNow.getUTCFullYear(), recapNow.getUTCMonth(), 1))
  );
  const recapYearStart = formatUtcDateInput(
    new Date(Date.UTC(recapNow.getUTCFullYear(), 0, 1))
  );

  const currentSessionStartedAt = currentSession
    ? new Date(currentSession.startedAt)
    : null;
  const currentSessionElapsed = currentSession
    ? formatDuration(getSessionDurationSeconds(currentSession), "long")
    : "0 min";

  const todaySessionCards = useMemo(
    () =>
      todaySessions.map((session) => ({
        sessionId: session.sessionId,
        gameId: session.gameId,
        title: session.game,
        time: formatDuration(getSessionDurationSeconds(session), "long"),
        platform: formatSessionPlatformLabel(session),
        imageUrl: session.capsuleUrl ?? null,
      })),
    [todaySessions]
  );
  const todayTotals = useMemo(() => {
    const totalSeconds = todayPlatformTotals.reduce(
      (sum, item) => sum + item.totalSeconds,
      0
    );
    return {
      seconds: totalSeconds,
      hours: formatHours(totalSeconds, 1),
    };
  }, [todayPlatformTotals]);
  const todayPlatforms = useMemo(() => {
    if (todayPlatformTotals.length === 0) {
      return [];
    }
    const totalSeconds = Math.max(todayTotals.seconds, 1);
    return todayPlatformTotals.map((item) => ({
      label: formatStatsPlatformLabel(item.platform),
      value: Math.round((item.totalSeconds / totalSeconds) * 100),
      hours: formatHours(item.totalSeconds, 1),
    }));
  }, [todayPlatformTotals, todayTotals.seconds]);
  const monthlyPlatforms = useMemo(() => {
    if (!monthlyStats) {
      return [];
    }
    const total = Math.max(monthlyStats.totalSeconds, 1);
    return monthlyStats.platforms.map((item) => ({
      label: formatStatsPlatformLabel(item.platform),
      value: Math.round((item.totalSeconds / total) * 100),
      hours: formatHours(item.totalSeconds, 1),
    }));
  }, [monthlyStats]);
  const monthlyTopGames = useMemo(() => {
    if (!monthlyStats) {
      return [];
    }
    return monthlyStats.topGames.map((game) => ({
      gameId: game.gameId,
      title: game.title,
      hours: formatHours(game.totalSeconds, 1),
    }));
  }, [monthlyStats]);
  const monthlySummary = useMemo(() => {
    if (!monthlyStats) {
      return {
        hours: "0h",
        sessions: "0",
        topTitle: "—",
      };
    }
    return {
      hours: formatHours(monthlyStats.totalSeconds, 1),
      sessions: String(monthlyStats.sessionCount),
      topTitle: monthlyStats.topGameTitle ?? "—",
    };
  }, [monthlyStats]);
  const monthlyHighlightsLabel = useMemo(() => {
    const monthName = new Intl.DateTimeFormat("en-US", {
      month: "long",
    }).format(new Date());
    return `${monthName} highlights`;
  }, []);
  const yearlyPlatforms = useMemo(() => {
    if (!yearlyStats) {
      return [];
    }
    const total = Math.max(yearlyStats.totalSeconds, 1);
    return yearlyStats.platforms.map((item) => ({
      label: formatStatsPlatformLabel(item.platform),
      value: Math.round((item.totalSeconds / total) * 100),
      hours: formatHours(item.totalSeconds, 1),
    }));
  }, [yearlyStats]);
  const yearlyTopGames = useMemo(() => {
    if (!yearlyStats) {
      return [];
    }
    return yearlyStats.topGames.map((game) => ({
      gameId: game.gameId,
      title: game.title,
      hours: formatHours(game.totalSeconds, 1),
    }));
  }, [yearlyStats]);
  const yearlySummary = useMemo(() => {
    if (!yearlyStats) {
      return {
        hours: "0h",
        sessions: "0",
        topTitle: "—",
      };
    }
    return {
      hours: formatHours(yearlyStats.totalSeconds, 1),
      sessions: String(yearlyStats.sessionCount),
      topTitle: yearlyStats.topGameTitle ?? "—",
    };
  }, [yearlyStats]);
  const yearlyRecapLabel = useMemo(() => {
    return `${new Date().getFullYear()} recap`;
  }, []);

  const handleGameSelect = (gameId: string) => {
    onNavigate(buildGamePath(gameId));
  };

  return (
    <main className="relative min-h-screen overflow-hidden bg-slate-200 dark:bg-slate-950 text-slate-900 dark:text-slate-100">
      <Background />
      <div className="relative mx-auto w-full max-w-6xl p-3 sm:px-6 sm:py-12">
        <AppHeader
          title="Dashboard"
          navItems={navItems}
          path={path}
          onNavigate={onNavigate}
          onLogout={onLogout}
          theme={theme}
          onToggleTheme={onToggleTheme}
        />

        <section className="mt-10 space-y-6">
          <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 px-4 py-3 sm:px-6 sm:py-4">
            <div className="flex flex-wrap items-center justify-between gap-4">
              <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                Current session
              </p>
              <div className="flex flex-wrap items-center gap-3">
                {currentSession?.platform === "manual" ? (
                  <button
                    type="button"
                    onClick={handleManualSessionStop}
                    disabled={isManualStopLoading}
                    className="rounded-full border border-rose-500/60 px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.3em] text-rose-700 dark:text-rose-200 transition hover:border-rose-400 hover:text-rose-700 dark:hover:text-rose-100 disabled:cursor-not-allowed disabled:border-rose-500/40"
                  >
                    {isManualStopLoading ? "Stopping..." : "Stop session"}
                  </button>
                ) : (
                  <button
                    type="button"
                    onClick={() => setIsManualModalOpen(true)}
                    className="rounded-full border border-emerald-400/60 px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-200 transition hover:border-emerald-300"
                  >
                    Start session
                  </button>
                )}
                <span className="rounded-full border border-emerald-400/40 px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-200">
                  {currentSession ? "Live" : "Idle"}
                </span>
              </div>
            </div>
            {isDashboardLoading ? (
              <div className="mt-3 flex flex-wrap items-center justify-between gap-5">
                <div className="h-6 w-40 rounded-full bg-slate-200/70 dark:bg-slate-800/60" />
                <div className="flex flex-wrap items-center justify-end gap-4">
                  <div className="h-3 w-24 rounded-full bg-slate-200/70 dark:bg-slate-800/60" />
                  <div className="h-3 w-32 rounded-full bg-slate-200/70 dark:bg-slate-800/60" />
                  <div className="h-3 w-28 rounded-full bg-slate-200/70 dark:bg-slate-800/60" />
                </div>
              </div>
            ) : (
              <div className="mt-3 flex flex-wrap items-center justify-between gap-5 text-sm font-semibold text-slate-600 dark:text-slate-300">
                {currentSession ? (
                  <>
                    <button
                      type="button"
                      onClick={() => handleGameSelect(currentSession.gameId)}
                      className="text-left text-lg font-semibold text-slate-900 dark:text-white transition hover:text-emerald-700 dark:hover:text-emerald-200"
                    >
                      {currentSession.game}
                    </button>
                    <div className="flex flex-wrap items-center justify-end gap-4 text-sm font-semibold normal-case tracking-normal text-slate-600 dark:text-slate-300">
                      <span className="inline-flex items-center gap-2 text-slate-700 dark:text-slate-200">
                        <svg
                          aria-hidden="true"
                          viewBox="0 0 20 20"
                          className="h-3.5 w-3.5 text-emerald-700 dark:text-emerald-300"
                          fill="currentColor"
                        >
                          <path d="M3 5a2 2 0 012-2h10a2 2 0 012 2v2H3V5zm0 4h14v6a2 2 0 01-2 2H5a2 2 0 01-2-2V9z" />
                        </svg>
                        {formatSessionPlatformLabel(currentSession)}
                      </span>
                      <span className="inline-flex items-center gap-2 text-slate-600 dark:text-slate-300">
                        <svg
                          aria-hidden="true"
                          viewBox="0 0 20 20"
                          className="h-3.5 w-3.5 text-slate-500 dark:text-slate-400"
                          fill="currentColor"
                        >
                          <path d="M10 2a8 8 0 100 16 8 8 0 000-16zm1 4v4.59l3.3 3.3-1.42 1.42L9 11V6h2z" />
                        </svg>
                        Started{" "}
                        {currentSessionStartedAt
                          ? formatTimeLabel(currentSessionStartedAt)
                          : "--"}
                      </span>
                      <span className="inline-flex items-center gap-2 text-emerald-700 dark:text-emerald-200">
                        <svg
                          aria-hidden="true"
                          viewBox="0 0 20 20"
                          className="h-3.5 w-3.5 text-emerald-700 dark:text-emerald-300"
                          fill="currentColor"
                        >
                          <path d="M10 2a8 8 0 100 16 8 8 0 000-16zm-1 3h2v6h4v2H9V5z" />
                        </svg>
                        Elapsed {currentSessionElapsed}
                      </span>
                    </div>
                  </>
                ) : (
                  <>
                    <h2 className="text-sm font-semibold text-slate-700 dark:text-slate-200">
                      No session running right now.
                    </h2>
                  </>
                )}
              </div>
            )}
          </div>

          <div className="grid gap-6 lg:grid-cols-[0.9fr_1.1fr]">
            <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
              <div className="flex items-center justify-between">
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                    Today stats
                  </p>
                  <h2 className="text-xl font-semibold text-slate-900 dark:text-white">
                    {todayTotals.hours} played
                  </h2>
                </div>
                <button
                  type="button"
                  onClick={() =>
                    onNavigate(`/recap?from=${recapToday}&to=${recapToday}`)
                  }
                  className="flex h-9 w-9 items-center justify-center rounded-xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/60 text-slate-600 dark:text-slate-200 transition hover:border-emerald-300 hover:text-emerald-700 dark:hover:text-emerald-200"
                  aria-label="Open recap"
                  title="Open recap"
                >
                  <BarChart3 className="h-4 w-4" aria-hidden="true" />
                </button>
              </div>
              <div className="mt-5 rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4">
                <p className="text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                  Platform distribution
                </p>
                {todayPlatforms.length === 0 ? (
                  <p className="mt-3 text-xs text-slate-500 dark:text-slate-400">
                    No sessions logged today yet.
                  </p>
                ) : (
                  <div className="mt-4 flex flex-wrap items-center gap-4">
                    <StatsPieChart
                      segments={todayPlatforms}
                      colors={platformPieColors}
                      size={140}
                    />
                    <div className="flex-1 space-y-2 text-xs text-slate-600 dark:text-slate-300">
                      {todayPlatforms.map((item, index) => (
                        <div
                          key={item.label}
                          className="flex items-center justify-between gap-3"
                        >
                          <div className="flex items-center gap-2">
                            <span
                              className="h-2 w-2 rounded-full"
                              style={{
                                backgroundColor:
                                  platformPieColors[
                                    index % platformPieColors.length
                                  ],
                              }}
                            />
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
                    {todaySessionCards.length}{" "}
                    {todaySessionCards.length === 1 ? "session" : "sessions"}{" "}
                    logged
                  </h2>
                </div>
                <button
                  type="button"
                  onClick={() => onNavigate("/sessions")}
                  className="rounded-full border border-slate-300 dark:border-slate-700 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-700 dark:text-slate-200 transition hover:border-emerald-400/60"
                >
                  View all
                </button>
              </div>
              <div className="mt-5 space-y-4">
                {dashboardError ? (
                  <div className="rounded-2xl border border-rose-500/30 bg-rose-500/10 p-4 text-xs text-rose-700 dark:text-rose-200">
                    {dashboardError}
                  </div>
                ) : isDashboardLoading ? (
                  [1, 2, 3].map((item) => (
                    <div
                      key={item}
                      className="flex items-center justify-between gap-4 rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4"
                    >
                      <div className="flex items-center gap-3">
                        <div className="h-14 w-10 rounded-xl bg-slate-200/70 dark:bg-slate-800/60" />
                        <div className="space-y-2">
                          <div className="h-3 w-28 rounded-full bg-slate-200/70 dark:bg-slate-800/70" />
                          <div className="h-2 w-16 rounded-full bg-slate-200/50 dark:bg-slate-800/50" />
                        </div>
                      </div>
                      <div className="h-3 w-12 rounded-full bg-slate-200/70 dark:bg-slate-800/60" />
                    </div>
                  ))
                ) : todaySessionCards.length === 0 ? (
                  <div className="rounded-2xl border border-dashed border-slate-200 dark:border-slate-800 bg-slate-200/40 dark:bg-slate-950/40 p-4 text-xs text-slate-500 dark:text-slate-400">
                    No sessions logged today yet.
                  </div>
                ) : (
                  todaySessionCards.map((session) => (
                    <div
                      key={session.sessionId}
                      className="relative flex flex-wrap items-center justify-between gap-4 overflow-hidden rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4"
                      style={
                        session.imageUrl
                          ? {
                              backgroundImage: `url(${session.imageUrl})`,
                              backgroundSize: "cover",
                              backgroundPosition: "center",
                            }
                          : undefined
                      }
                    >
                      <div
                        className={`absolute inset-0 ${
                          session.imageUrl
                            ? "bg-white/90 dark:bg-slate-950/80"
                            : "bg-white/90 dark:bg-slate-900/70"
                        }`}
                      />
                      <div className="relative z-10 flex items-center gap-3">
                        {!session.imageUrl ? (
                          <div className="h-14 w-10 rounded-xl bg-slate-200/70 dark:bg-slate-800/60" />
                        ) : null}
                        <button
                          type="button"
                          onClick={() => handleGameSelect(session.gameId)}
                          className="text-left text-sm font-semibold text-slate-900 dark:text-white transition hover:text-emerald-700 dark:hover:text-emerald-200"
                        >
                          {session.title}
                        </button>
                      </div>
                      <div className="relative z-10 flex items-center gap-4">
                        <p className="text-xs text-slate-500 dark:text-slate-400">
                          {session.platform}
                        </p>
                        <p className="text-sm font-semibold text-emerald-700 dark:text-emerald-200">
                          {session.time}
                        </p>
                      </div>
                    </div>
                  ))
                )}
              </div>
            </div>
          </div>

          <StatsRecapSection
            title="Monthly stats"
            subtitle={monthlyHighlightsLabel}
            summary={[
              { label: "Hours played", value: monthlySummary.hours },
              { label: "Sessions", value: monthlySummary.sessions },
              { label: "Top title", value: monthlySummary.topTitle },
            ]}
            platforms={monthlyPlatforms}
            topGames={monthlyTopGames}
            colors={platformPieColors}
            onGameSelect={handleGameSelect}
            onOpenRecap={() =>
              onNavigate(`/recap?from=${recapMonthStart}&to=${recapToday}`)
            }
          />

          <StatsRecapSection
            title="Yearly stats"
            subtitle={yearlyRecapLabel}
            summary={[
              { label: "Hours played", value: yearlySummary.hours },
              { label: "Sessions", value: yearlySummary.sessions },
              { label: "Top title", value: yearlySummary.topTitle },
            ]}
            platforms={yearlyPlatforms}
            topGames={yearlyTopGames}
            colors={platformPieColors}
            onGameSelect={handleGameSelect}
            onOpenRecap={() =>
              onNavigate(`/recap?from=${recapYearStart}&to=${recapToday}`)
            }
          />
        </section>
        <AppFooter />
      </div>
      <ManualSessionModal
        isOpen={isManualModalOpen}
        manualInputs={manualInputs}
        manualInputsError={manualInputsError}
        manualInputSelection={manualInputSelection}
        manualInputText={manualInputText}
        manualStartError={manualStartError}
        isManualInputsLoading={isManualInputsLoading}
        isManualStartLoading={isManualStartLoading}
        onClose={closeManualModal}
        onSelectInput={(value) => {
          setManualInputSelection(value);
          setManualInputText(value);
        }}
        onInputTextChange={(value) => setManualInputText(value)}
        onStart={handleManualSessionStart}
      />
    </main>
  );
};
