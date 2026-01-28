import { useEffect, useMemo, useState } from "react";

import { apiRequest } from "../api";
import { buildGamePath, navItems as defaultNavItems } from "../constants";
import type { NavItem, RecapResponse, RecapYearsResponse } from "../types";
import {
  formatDateLabel,
  formatDuration,
  formatHours,
  formatStatsPlatformLabel,
} from "../utils/formatters";
import { AppHeader } from "../components/AppHeader";
import { Background } from "../components/Background";
import { StatsRecapSection } from "../components/StatsRecapSection";

type RecapPageProps = {
  path: string;
  search: string;
  navItems?: NavItem[];
  onNavigate: (path: string) => void;
  onLogout: () => void;
  theme: "light" | "dark";
  onToggleTheme: () => void;
};

type RecapRange = { from: string; to: string } | null;

const weekdayLabels = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

const formatMonthLabel = (monthKey: string) => {
  const [year, month] = monthKey.split("-").map((value) => Number(value));
  if (!year || !month) {
    return monthKey;
  }
  const date = new Date(Date.UTC(year, month - 1, 1));
  return date.toLocaleDateString("en-US", { month: "short", year: "numeric" });
};

const formatAverage = (value: number, decimals = 1) => {
  if (!Number.isFinite(value)) {
    return "0";
  }
  const rounded = Number(value.toFixed(decimals));
  return `${rounded}`.replace(/\.0$/, "");
};

export const RecapPage = ({
  path,
  search,
  navItems = defaultNavItems,
  onNavigate,
  onLogout,
  theme,
  onToggleTheme,
}: RecapPageProps) => {
  const [years, setYears] = useState<number[]>([]);
  const [yearsLoaded, setYearsLoaded] = useState(false);
  const [activeTab, setActiveTab] = useState("all-time");
  const [customFrom, setCustomFrom] = useState("");
  const [customTo, setCustomTo] = useState("");
  const [recap, setRecap] = useState<RecapResponse | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState("");
  const [hasDefaultLoaded, setHasDefaultLoaded] = useState(false);
  const [hasSearchRange, setHasSearchRange] = useState(false);

  const loadRecap = async (range: RecapRange) => {
    setIsLoading(true);
    setError("");
    try {
      const params = new URLSearchParams();
      if (range?.from && range?.to) {
        params.set("from", range.from);
        params.set("to", range.to);
      }
      const path = params.toString()
        ? `/recap?${params.toString()}`
        : "/recap";
      const data = await apiRequest<RecapResponse>(path, {
        onAuthFailure: onLogout,
      });
      setRecap(data);
      setIsLoading(false);
    } catch (err) {
      setRecap(null);
      setIsLoading(false);
      setError(err instanceof Error ? err.message : "Failed to load recap.");
    }
  };

  useEffect(() => {
    const loadYears = async () => {
      setYearsLoaded(false);
      try {
        const response = await apiRequest<RecapYearsResponse>("/recap/years", {
          onAuthFailure: onLogout,
        });
        setYears(response.years);
        setYearsLoaded(true);
      } catch (err) {
        setYears([]);
        setYearsLoaded(true);
        setError(
          err instanceof Error ? err.message : "Failed to load recap years."
        );
      }
    };

    void loadYears();
  }, [onLogout]);

  useEffect(() => {
    const params = new URLSearchParams(search);
    const from = params.get("from");
    const to = params.get("to");
    if (from && to) {
      setHasSearchRange(true);
      setCustomFrom(from);
      setCustomTo(to);
      setActiveTab("custom");
      void loadRecap({ from, to });
      return;
    }
    setHasSearchRange(false);
    setHasDefaultLoaded(false);
  }, [search]);

  useEffect(() => {
    if (!yearsLoaded || hasSearchRange || hasDefaultLoaded) {
      return;
    }
    setActiveTab("all-time");
    void loadRecap(null);
    setHasDefaultLoaded(true);
  }, [yearsLoaded, hasSearchRange, hasDefaultLoaded]);

  const handleTabSelect = (tabKey: string, range: RecapRange) => {
    setActiveTab(tabKey);
    if (tabKey === "custom") {
      if (customFrom && customTo) {
        void loadRecap({ from: customFrom, to: customTo });
      }
      return;
    }
    void loadRecap(range);
  };

  const recapTabs = useMemo(() => {
    const yearTabs = years.map((year) => ({
      key: `year-${year}`,
      label: String(year),
      range: { from: `${year}-01-01`, to: `${year}-12-31` },
    }));
    return [
      { key: "all-time", label: "All time", range: null },
      ...yearTabs,
      { key: "custom", label: "Custom", range: null },
    ];
  }, [years]);

  const rangeLabel = useMemo(() => {
    if (!recap) {
      return "";
    }
    if (!recap.range.from || !recap.range.to) {
      return "All time";
    }
    const fromDate = formatDateLabel(new Date(`${recap.range.from}T00:00:00Z`));
    const toDate = formatDateLabel(new Date(`${recap.range.to}T00:00:00Z`));
    return `${fromDate} — ${toDate}`;
  }, [recap]);

  const recapSummary = useMemo(() => {
    if (!recap) {
      return {
        hours: "0h",
        sessions: "0",
        topTitle: "—",
      };
    }
    return {
      hours: formatHours(recap.totalSeconds, 1),
      sessions: String(recap.totalSessions),
      topTitle: recap.topGameByTime?.title ?? "—",
    };
  }, [recap]);

  const recapPlatforms = useMemo(() => {
    if (!recap) {
      return [];
    }
    const total = Math.max(recap.totalSeconds, 1);
    return recap.platformTotals.map((item) => ({
      label: formatStatsPlatformLabel(item.platform),
      value: Math.round((item.totalSeconds / total) * 100),
      hours: formatHours(item.totalSeconds, 1),
    }));
  }, [recap]);

  const recapTopGames = useMemo(() => {
    if (!recap) {
      return [];
    }
    return recap.topGamesByTime.map((game) => ({
      gameId: game.gameId,
      title: game.title,
      hours: formatHours(game.totalSeconds, 1),
    }));
  }, [recap]);

  const weekdayStats = recap?.sessionsByWeekday ?? [];
  const weekdayMax = Math.max(
    1,
    ...weekdayStats.map((item) => item.sessionCount)
  );
  const hourStats = recap?.sessionsByHour ?? [];
  const hourMax = Math.max(1, ...hourStats.map((item) => item.sessionCount));

  const totalDays = (recap?.daysPlayed ?? 0) + (recap?.daysNotPlayed ?? 0);
  const playedPercent =
    totalDays > 0 ? ((recap?.daysPlayed ?? 0) / totalDays) * 100 : 0;

  const platformRows = recap?.platformTotals ?? [];

  return (
    <main className="relative min-h-screen overflow-hidden bg-slate-200 dark:bg-slate-950 text-slate-900 dark:text-slate-100">
      <Background />
      <div className="relative mx-auto w-full max-w-6xl p-3 sm:px-6 sm:py-12">
        <AppHeader
          title="Recap"
          navItems={navItems}
          path={path}
          onNavigate={onNavigate}
          onLogout={onLogout}
          theme={theme}
          onToggleTheme={onToggleTheme}
        />

        <section className="mt-10 space-y-6">
          <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-4 sm:p-5">
            <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
              Recap tabs
            </p>
            <div className="mt-4 flex flex-wrap gap-2">
              {recapTabs.map((tab) => (
                <button
                  key={tab.key}
                  type="button"
                  onClick={() => handleTabSelect(tab.key, tab.range)}
                  className={`rounded-full border px-3 py-2 text-[10px] font-semibold uppercase tracking-[0.25em] transition ${
                    activeTab === tab.key
                      ? "border-emerald-300 bg-emerald-400 text-slate-900"
                      : "border-slate-200 dark:border-slate-800 text-slate-600 dark:text-slate-200 hover:border-emerald-300/60"
                  }`}
                >
                  {tab.label}
                </button>
              ))}
            </div>
          </div>

          <div className="space-y-6">
            {activeTab === "custom" ? (
              <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                      Custom recap
                    </p>
                    <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
                      Choose your range
                    </h2>
                  </div>
                </div>
                <div className="mt-5 grid gap-3 md:grid-cols-[1fr_1fr_auto]">
                  <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
                    From
                    <input
                      type="date"
                      value={customFrom}
                      onChange={(event) => setCustomFrom(event.target.value)}
                      className="mt-2 w-full rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                    />
                  </label>
                  <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
                    To
                    <input
                      type="date"
                      value={customTo}
                      onChange={(event) => setCustomTo(event.target.value)}
                      className="mt-2 w-full rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                    />
                  </label>
                  <button
                    type="button"
                    onClick={() => {
                      if (customFrom && customTo) {
                        onNavigate(`/recap?from=${customFrom}&to=${customTo}`);
                      }
                    }}
                    className="mt-6 rounded-full border border-emerald-400/60 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-200 transition hover:border-emerald-300"
                  >
                    Apply
                  </button>
                </div>
              </div>
            ) : null}

            {error ? (
              <p className="text-xs text-rose-700 dark:text-rose-300">{error}</p>
            ) : null}

            {recap ? (
              <StatsRecapSection
                title="Recap overview"
                subtitle={rangeLabel}
                summary={[
                  { label: "Hours played", value: recapSummary.hours },
                  { label: "Sessions", value: recapSummary.sessions },
                  { label: "Top title", value: recapSummary.topTitle },
                ]}
                platforms={recapPlatforms}
                topGames={recapTopGames}
                colors={
                  theme === "dark"
                    ? ["#34d399", "#22d3ee", "#a78bfa", "#f59e0b"]
                    : ["#059669", "#0e7490", "#7c3aed", "#d97706"]
                }
                onGameSelect={(gameId) => onNavigate(buildGamePath(gameId))}
              />
            ) : null}

            {isLoading ? (
              <p className="text-xs text-slate-500 dark:text-slate-400">
                Loading recap...
              </p>
            ) : null}

            {recap ? (
              <>
                <div className="grid gap-4 lg:grid-cols-3">
                  {[
                    { label: "Total sessions", value: String(recap.totalSessions) },
                    { label: "Total time", value: formatHours(recap.totalSeconds, 1) },
                    {
                      label: "Avg session length",
                      value: formatDuration(recap.averageSessionSeconds, "long"),
                    },
                    {
                      label: "Median session length",
                      value: formatDuration(recap.medianSessionSeconds, "long"),
                    },
                    {
                      label: "Longest session",
                      value: formatDuration(recap.longestSessionSeconds, "long"),
                    },
                    {
                      label: "Shortest session",
                      value: formatDuration(recap.shortestSessionSeconds, "long"),
                    },
                  ].map((item) => (
                    <div
                      key={item.label}
                      className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-4"
                    >
                      <p className="text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                        {item.label}
                      </p>
                      <p className="mt-2 text-lg font-semibold text-slate-900 dark:text-white">
                        {item.value}
                      </p>
                    </div>
                  ))}
                </div>

                <div className="grid gap-4 lg:grid-cols-3">
                  {[
                    {
                      label: "Avg sessions / day",
                      value: formatAverage(recap.averageSessionsPerDay),
                    },
                    {
                      label: "Avg sessions / week",
                      value: formatAverage(recap.averageSessionsPerWeek),
                    },
                    {
                      label: "Avg sessions / month",
                      value: formatAverage(recap.averageSessionsPerMonth),
                    },
                    {
                      label: "Most active day",
                      value: recap.mostActiveDay
                        ? `${formatDateLabel(
                            new Date(`${recap.mostActiveDay.date}T00:00:00Z`)
                          )} · ${recap.mostActiveDay.sessionCount}`
                        : "—",
                    },
                    {
                      label: "Most active month",
                      value: recap.mostActiveMonth
                        ? `${formatMonthLabel(
                            recap.mostActiveMonth.month
                          )} · ${recap.mostActiveMonth.sessionCount}`
                        : "—",
                    },
                    {
                      label: "Longest streak",
                      value: `${recap.longestStreakDays} days`,
                    },
                  ].map((item) => (
                    <div
                      key={item.label}
                      className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-4"
                    >
                      <p className="text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                        {item.label}
                      </p>
                      <p className="mt-2 text-lg font-semibold text-slate-900 dark:text-white">
                        {item.value}
                      </p>
                    </div>
                  ))}
                </div>

                <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                  <div className="flex flex-wrap items-center justify-between gap-3">
                    <div>
                      <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                        Activity
                      </p>
                      <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
                        Days played vs not played
                      </h2>
                    </div>
                    <div className="text-xs font-semibold uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                      {recap.daysPlayed} / {totalDays} days played
                    </div>
                  </div>
                  <div className="mt-5 h-3 w-full overflow-hidden rounded-full bg-slate-200/70 dark:bg-slate-800/60">
                    <div
                      className="h-full rounded-full bg-emerald-400/80"
                      style={{ width: `${Math.min(100, playedPercent)}%` }}
                    />
                  </div>
                  <div className="mt-4 grid gap-3 sm:grid-cols-2">
                    <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4">
                      <p className="text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                        Days played
                      </p>
                      <p className="mt-2 text-lg font-semibold text-slate-900 dark:text-white">
                        {recap.daysPlayed}
                      </p>
                    </div>
                    <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4">
                      <p className="text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                        Days not played
                      </p>
                      <p className="mt-2 text-lg font-semibold text-slate-900 dark:text-white">
                        {recap.daysNotPlayed}
                      </p>
                    </div>
                  </div>
                </div>

                <div className="grid gap-6 lg:grid-cols-[1.1fr_0.9fr]">
                  <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                    <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                      Sessions by weekday
                    </p>
                    <div className="mt-4 grid grid-cols-7 items-end gap-2">
                      {weekdayStats.map((item, index) => (
                        <div key={`weekday-${item.weekday}`} className="flex flex-col items-center gap-2">
                          <div className="flex h-28 items-end">
                            <div
                              className="w-4 rounded-full bg-emerald-400/80"
                              style={{
                                height: `${(item.sessionCount / weekdayMax) * 100}%`,
                              }}
                            />
                          </div>
                          <span className="text-[10px] uppercase tracking-[0.2em] text-slate-500 dark:text-slate-400">
                            {weekdayLabels[index] ?? item.weekday}
                          </span>
                        </div>
                      ))}
                    </div>
                  </div>

                  <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                    <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                      Hourly heatmap
                    </p>
                    <div className="mt-4 grid grid-cols-12 items-end gap-2">
                      {hourStats.map((item) => (
                        <div key={`hour-${item.hour}`} className="flex flex-col items-center gap-2">
                          <div className="flex h-20 items-end">
                            <div
                              className="w-2 rounded-full bg-emerald-400/80"
                              style={{
                                height: `${(item.sessionCount / hourMax) * 100}%`,
                              }}
                            />
                          </div>
                          <span className="text-[9px] uppercase tracking-[0.2em] text-slate-500 dark:text-slate-400">
                            {String(item.hour).padStart(2, "0")}
                          </span>
                        </div>
                      ))}
                    </div>
                    <div className="mt-4 text-xs text-slate-500 dark:text-slate-400">
                      Peak hour:{" "}
                      <span className="font-semibold text-slate-900 dark:text-white">
                        {recap.peakHour
                          ? `${String(recap.peakHour.hour).padStart(2, "0")}:00`
                          : "—"}
                      </span>
                    </div>
                  </div>
                </div>

                <div className="grid gap-4 lg:grid-cols-3">
                  {[
                    {
                      label: "Weekend play time",
                      value: formatHours(recap.weekendSeconds, 1),
                    },
                    {
                      label: "Weekday play time",
                      value: formatHours(recap.weekdaySeconds, 1),
                    },
                    {
                      label: "Most used platform",
                      value: recap.mostUsedPlatform
                        ? formatStatsPlatformLabel(recap.mostUsedPlatform.platform)
                        : "—",
                    },
                  ].map((item) => (
                    <div
                      key={item.label}
                      className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-4"
                    >
                      <p className="text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                        {item.label}
                      </p>
                      <p className="mt-2 text-lg font-semibold text-slate-900 dark:text-white">
                        {item.value}
                      </p>
                    </div>
                  ))}
                </div>

                <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                  <div className="flex flex-wrap items-center justify-between gap-3">
                    <div>
                      <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                        Platform breakdown
                      </p>
                      <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
                        Sessions and time by platform
                      </h2>
                    </div>
                  </div>
                  <div className="mt-5 space-y-3">
                    {platformRows.length === 0 ? (
                      <p className="text-sm text-slate-500 dark:text-slate-400">
                        No platform data available yet.
                      </p>
                    ) : (
                      platformRows.map((item) => (
                        <div
                          key={`platform-${item.platform}`}
                          className="flex items-center justify-between rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 px-4 py-3 text-sm"
                        >
                          <span className="font-semibold text-slate-900 dark:text-white">
                            {formatStatsPlatformLabel(item.platform)}
                          </span>
                          <span className="text-xs font-semibold text-emerald-700 dark:text-emerald-200">
                            {item.sessionCount} sessions · {formatHours(item.totalSeconds, 1)}
                          </span>
                        </div>
                      ))
                    )}
                  </div>
                </div>

                <div className="grid gap-4 lg:grid-cols-3">
                  {[
                    {
                      label: "Unique games played",
                      value: String(recap.uniqueGames),
                    },
                    {
                      label: "New games tried",
                      value: String(recap.newGamesTried),
                    },
                    {
                      label: "Games abandoned",
                      value: String(recap.abandonedGames),
                    },
                    {
                      label: "Return rate games",
                      value: String(recap.returnRateGames),
                    },
                    {
                      label: "Top 3 time share",
                      value: `${Math.round(recap.timeOnTop3Percent)}%`,
                    },
                    {
                      label: "Most binged game",
                      value: recap.mostBingedGame
                        ? `${recap.mostBingedGame.title} · ${recap.mostBingedGame.sessionCount}`
                        : "—",
                    },
                  ].map((item) => (
                    <div
                      key={item.label}
                      className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-4"
                    >
                      <p className="text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                        {item.label}
                      </p>
                      <p className="mt-2 text-lg font-semibold text-slate-900 dark:text-white">
                        {item.value}
                      </p>
                    </div>
                  ))}
                </div>

                <div className="grid gap-4 lg:grid-cols-3">
                  {[
                    {
                      label: "Top game by sessions",
                      value: recap.topGameBySessions
                        ? `${recap.topGameBySessions.title} · ${recap.topGameBySessions.sessionCount}`
                        : "—",
                    },
                    {
                      label: "Top game by time",
                      value: recap.topGameByTime
                        ? `${recap.topGameByTime.title} · ${formatHours(
                            recap.topGameByTime.totalSeconds,
                            1
                          )}`
                        : "—",
                    },
                    {
                      label: "Top game by avg session",
                      value: recap.topGameByAverageSession
                        ? `${recap.topGameByAverageSession.title} · ${formatDuration(
                            recap.topGameByAverageSession.averageSessionSeconds,
                            "long"
                          )}`
                        : "—",
                    },
                    {
                      label: "Longest session game",
                      value: recap.gameWithLongestSession
                        ? `${recap.gameWithLongestSession.title} · ${formatDuration(
                            recap.gameWithLongestSession.sessionSeconds,
                            "long"
                          )}`
                        : "—",
                    },
                    {
                      label: "Longest session length",
                      value: recap.gameWithLongestSession
                        ? formatDuration(
                            recap.gameWithLongestSession.sessionSeconds,
                            "long"
                          )
                        : "—",
                    },
                    {
                      label: "Peak hour sessions",
                      value: recap.peakHour
                        ? `${String(recap.peakHour.hour).padStart(2, "0")}:00 · ${
                            recap.peakHour.sessionCount
                          }`
                        : "—",
                    },
                  ].map((item) => (
                    <div
                      key={item.label}
                      className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-4"
                    >
                      <p className="text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                        {item.label}
                      </p>
                      <p className="mt-2 text-lg font-semibold text-slate-900 dark:text-white">
                        {item.value}
                      </p>
                    </div>
                  ))}
                </div>

                <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                  <div className="flex flex-wrap items-center justify-between gap-3">
                    <div>
                      <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                        Full library
                      </p>
                      <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
                        Summed playtime by game
                      </h2>
                    </div>
                  </div>
                  <div className="mt-5 max-h-96 space-y-2 overflow-auto pr-1">
                    {recap.gamesTable.length === 0 ? (
                      <p className="text-sm text-slate-500 dark:text-slate-400">
                        No games logged yet.
                      </p>
                    ) : (
                      recap.gamesTable.map((game, index) => (
                        <div
                          key={`recap-game-${game.gameId}`}
                          className="flex items-center justify-between rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 px-4 py-3 text-sm"
                        >
                          <div className="flex items-center gap-3">
                            <span className="text-xs font-semibold text-slate-500 dark:text-slate-400">
                              {String(index + 1).padStart(2, "0")}
                            </span>
                            <button
                              type="button"
                              onClick={() => onNavigate(buildGamePath(game.gameId))}
                              className="text-left font-semibold text-slate-900 dark:text-white transition hover:text-emerald-700 dark:hover:text-emerald-200"
                            >
                              {game.title}
                            </button>
                          </div>
                          <span className="text-xs font-semibold text-emerald-700 dark:text-emerald-200">
                            {formatHours(game.totalSeconds, 1)} · {game.sessionCount} sessions
                          </span>
                        </div>
                      ))
                    )}
                  </div>
                </div>
              </>
            ) : null}
          </div>
        </section>
      </div>
    </main>
  );
};
