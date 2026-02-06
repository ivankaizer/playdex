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
const platformBarClasses = [
  "bg-emerald-400",
  "bg-teal-400",
  "bg-cyan-400",
  "bg-sky-400",
  "bg-indigo-400",
  "bg-violet-400",
];
const platformDotClasses = [
  "bg-emerald-400/80",
  "bg-teal-400/80",
  "bg-cyan-400/80",
  "bg-sky-400/80",
  "bg-indigo-400/80",
  "bg-violet-400/80",
];

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

  const recapTopGames = useMemo(() => {
    if (!recap) {
      return [];
    }
    return recap.topGamesByTime.map((game) => ({
      gameId: game.gameId,
      title: game.title,
      capsuleUrl: game.capsuleUrl,
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

  const daysPlayed = recap?.daysPlayed ?? 0;
  const daysNotPlayed = recap?.daysNotPlayed ?? 0;
  const totalDays = daysPlayed + daysNotPlayed;
  const daysPlayedPercent = totalDays > 0 ? (daysPlayed / totalDays) * 100 : 0;
  const platformRows = recap?.platformTotals ?? [];
  const platformTotalSeconds = platformRows.reduce(
    (sum, item) => sum + item.totalSeconds,
    0
  );
  const timeSplitTotalSeconds =
    (recap?.weekendSeconds ?? 0) + (recap?.weekdaySeconds ?? 0);
  const weekendShare =
    timeSplitTotalSeconds > 0
      ? (recap?.weekendSeconds ?? 0) / timeSplitTotalSeconds
      : 0;
  const weekdayShare =
    timeSplitTotalSeconds > 0
      ? (recap?.weekdaySeconds ?? 0) / timeSplitTotalSeconds
      : 0;
  const daysPlayedPercentRounded = Math.round(daysPlayedPercent);
  const weekendSharePercent = Math.round(weekendShare * 100);
  const weekdaySharePercent = Math.round(weekdayShare * 100);

  const weekdayChart = useMemo(() => {
    const width = 240;
    const height = 80;
    const padding = 8;
    if (weekdayStats.length === 0) {
      return { width, height, linePoints: "", areaPath: "", points: [] };
    }
    const step =
      (width - padding * 2) / Math.max(1, weekdayStats.length - 1);
    const points = weekdayStats.map((item, index) => {
      const x = padding + index * step;
      const y =
        height -
        padding -
        (item.sessionCount / weekdayMax) * (height - padding * 2);
      return { x, y };
    });
    const linePoints = points.map((point) => `${point.x},${point.y}`).join(" ");
    const areaPath = `M ${padding},${height - padding} L ${linePoints} L ${
      width - padding
    },${height - padding} Z`;
    return { width, height, linePoints, areaPath, points };
  }, [weekdayStats, weekdayMax]);

  const volumeStats = recap
    ? [
        { label: "Total sessions", value: String(recap.totalSessions) },
        { label: "Total time", value: formatHours(recap.totalSeconds, 1) },
      ]
    : [];

  const sessionLengthStats = recap
    ? [
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
      ]
    : [];

  const cadenceStats = recap
    ? [
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
            ? `${formatMonthLabel(recap.mostActiveMonth.month)} · ${
                recap.mostActiveMonth.sessionCount
              }`
            : "—",
        },
        {
          label: "Longest streak",
          value: `${recap.longestStreakDays} days`,
        },
      ]
    : [];

  const gameMixStats = recap
    ? [
        { label: "Unique games played", value: String(recap.uniqueGames) },
        { label: "New games tried", value: String(recap.newGamesTried) },
        { label: "Games abandoned", value: String(recap.abandonedGames) },
        { label: "Return rate games", value: String(recap.returnRateGames) },
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
      ]
    : [];

  const gameHighlightStats = recap
    ? [
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
      ]
    : [];

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
              <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                      Most popular
                    </p>
                    <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
                      Top 5 games
                    </h2>
                  </div>
                  <div className="text-xs font-semibold uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                    {rangeLabel}
                  </div>
                </div>
                <div className="mt-5 grid gap-3 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-5">
                  {recapTopGames.length === 0 ? (
                    <div className="rounded-2xl border border-dashed border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4 text-sm text-slate-500 dark:text-slate-400 lg:col-span-5">
                      No game data available yet.
                    </div>
                  ) : (
                    recapTopGames.map((game) => (
                      <button
                        key={`recap-top-game-${game.gameId}`}
                        type="button"
                        onClick={() => onNavigate(buildGamePath(game.gameId))}
                        className="group text-left"
                      >
                        <div className="w-full">
                          <div className="overflow-hidden rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50">
                            {game.capsuleUrl ? (
                              <img
                                src={game.capsuleUrl}
                                alt={`${game.title} capsule art`}
                                className="aspect-[2/3] w-full object-cover"
                              />
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
                              {game.hours}
                            </span>
                          </div>
                        </div>
                      </button>
                    ))
                  )}
                </div>
              </div>
            ) : null}

            {isLoading ? (
              <p className="text-xs text-slate-500 dark:text-slate-400">
                Loading recap...
              </p>
            ) : null}

            {recap ? (
              <>
                <div className="grid gap-6 lg:grid-cols-2 lg:items-stretch">
                  <div className="h-full rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                    <div className="flex flex-wrap items-center justify-between gap-3">
                      <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                          Session overview
                        </p>
                        <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
                          Volume and session length
                        </h2>
                      </div>
                    </div>
                    <div className="mt-4 grid gap-4">
                      <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/40 p-4">
                        <p className="text-xs font-semibold uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                          Volume
                        </p>
                        <div className="mt-3 grid gap-2 text-sm">
                          {volumeStats.map((item) => (
                            <div key={item.label} className="flex items-center justify-between">
                              <span className="text-[10px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                                {item.label}
                              </span>
                              <span className="font-semibold text-slate-900 dark:text-white">
                                {item.value}
                              </span>
                            </div>
                          ))}
                        </div>
                      </div>
                      <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/40 p-4">
                        <p className="text-xs font-semibold uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                          Session length
                        </p>
                        <div className="mt-3 grid gap-2 text-sm">
                          {sessionLengthStats.map((item) => (
                            <div key={item.label} className="flex items-center justify-between">
                              <span className="text-[10px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                                {item.label}
                              </span>
                              <span className="font-semibold text-slate-900 dark:text-white">
                                {item.value}
                              </span>
                            </div>
                          ))}
                        </div>
                      </div>
                    </div>
                  </div>

                  <div className="h-full rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                    <div className="flex flex-wrap items-center justify-between gap-3">
                      <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                          Consistency
                        </p>
                        <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
                          Days played
                        </h2>
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
                          />
                          <circle
                            cx="60"
                            cy="60"
                            r="46"
                            fill="none"
                            stroke="currentColor"
                            strokeWidth="12"
                            strokeLinecap="round"
                            strokeDasharray={`${daysPlayedPercent} ${100 - daysPlayedPercent}`}
                            pathLength="100"
                            className="text-emerald-400"
                          />
                        </svg>
                        <div className="absolute text-center">
                          <p className="text-2xl font-semibold text-slate-900 dark:text-white">
                            {daysPlayedPercentRounded}%
                          </p>
                          <p className="text-[10px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                            played
                          </p>
                        </div>
                      </div>
                      <div className="grid gap-2 text-sm">
                        <div className="flex items-center justify-between">
                          <span className="text-[10px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                            Days played
                          </span>
                          <span className="font-semibold text-slate-900 dark:text-white">
                            {daysPlayed}
                          </span>
                        </div>
                        <div className="flex items-center justify-between">
                          <span className="text-[10px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                            Days off
                          </span>
                          <span className="font-semibold text-slate-900 dark:text-white">
                            {daysNotPlayed}
                          </span>
                        </div>
                        <div className="flex items-center justify-between">
                          <span className="text-[10px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                            Play rate
                          </span>
                          <span className="font-semibold text-slate-900 dark:text-white">
                            {daysPlayedPercentRounded}%
                          </span>
                        </div>
                      </div>
                    </div>
                    <div className="mt-5 grid gap-3 sm:grid-cols-2">
                      {cadenceStats.map((item) => (
                        <div
                          key={item.label}
                          className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/40 p-3"
                        >
                          <p className="text-[10px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                            {item.label}
                          </p>
                          <p className="mt-2 text-sm font-semibold text-slate-900 dark:text-white">
                            {item.value}
                          </p>
                        </div>
                      ))}
                    </div>
                  </div>
                </div>

                <div className="grid gap-6 lg:grid-cols-2 lg:items-stretch">
                  <div className="h-full rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                    <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                      Weekly rhythm
                    </p>
                    <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
                      Sessions by weekday
                    </h2>
                    <div className="mt-4">
                      <svg
                        className="h-24 w-full"
                        viewBox={`0 0 ${weekdayChart.width} ${weekdayChart.height}`}
                        preserveAspectRatio="none"
                      >
                        <defs>
                          <linearGradient id="weekdayGradient" x1="0" x2="0" y1="0" y2="1">
                            <stop offset="0%" stopColor="rgba(52, 211, 153, 0.4)" />
                            <stop offset="100%" stopColor="rgba(52, 211, 153, 0)" />
                          </linearGradient>
                        </defs>
                        {weekdayChart.areaPath ? (
                          <path
                            d={weekdayChart.areaPath}
                            fill="url(#weekdayGradient)"
                            stroke="none"
                          />
                        ) : null}
                        {weekdayChart.linePoints ? (
                          <polyline
                            points={weekdayChart.linePoints}
                            fill="none"
                            stroke="rgba(16, 185, 129, 0.9)"
                            strokeWidth="2.5"
                            strokeLinecap="round"
                            strokeLinejoin="round"
                          />
                        ) : null}
                        {weekdayChart.points.map((point, index) => (
                          <circle
                            key={`weekday-point-${index}`}
                            cx={point.x}
                            cy={point.y}
                            r="2.5"
                            fill="rgba(16, 185, 129, 0.9)"
                          />
                        ))}
                      </svg>
                      <div className="mt-3 grid grid-cols-7 text-[10px] uppercase tracking-[0.2em] text-slate-500 dark:text-slate-400">
                        {weekdayLabels.map((label) => (
                          <span key={`weekday-label-${label}`} className="text-center">
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
                    <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
                      Heatmap by hour
                    </h2>
                    <div className="mt-4 grid grid-cols-12 gap-2">
                      {hourStats.map((item) => {
                        const intensity = hourMax > 0 ? item.sessionCount / hourMax : 0;
                        const opacity = 0.2 + intensity * 0.8;
                        return (
                          <div key={`hour-${item.hour}`} className="flex flex-col items-center gap-2">
                            <span
                              className="h-3 w-3 rounded-full bg-emerald-400"
                              style={{ opacity }}
                            />
                            <span className="text-[9px] uppercase tracking-[0.2em] text-slate-500 dark:text-slate-400">
                              {String(item.hour).padStart(2, "0")}
                            </span>
                          </div>
                        );
                      })}
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

                <div className="grid gap-6 lg:grid-cols-2 lg:items-stretch">
                  <div className="h-full rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                    <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                      Time split
                    </p>
                    <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
                      Weekend vs weekday
                    </h2>
                    <div className="mt-4 space-y-3">
                      <div className="flex items-center justify-between text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                        <span>Weekend</span>
                        <span className="font-semibold text-slate-900 dark:text-white">
                          {formatHours(recap.weekendSeconds, 1)} · {weekendSharePercent}%
                        </span>
                      </div>
                      <div className="flex h-3 w-full overflow-hidden rounded-full bg-slate-200/70 dark:bg-slate-800/70">
                        <div
                          className="h-full bg-emerald-400"
                          style={{ width: `${weekendShare * 100}%` }}
                        />
                        <div
                          className="h-full bg-sky-400"
                          style={{ width: `${weekdayShare * 100}%` }}
                        />
                      </div>
                      <div className="flex items-center justify-between text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                        <span>Weekday</span>
                        <span className="font-semibold text-slate-900 dark:text-white">
                          {formatHours(recap.weekdaySeconds, 1)} · {weekdaySharePercent}%
                        </span>
                      </div>
                    </div>
                    <div className="mt-6 grid gap-3 sm:grid-cols-2">
                      <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/40 p-3">
                        <p className="text-[10px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                          Peak hour
                        </p>
                        <p className="mt-2 text-sm font-semibold text-slate-900 dark:text-white">
                          {recap.peakHour
                            ? `${String(recap.peakHour.hour).padStart(2, "0")}:00 · ${
                                recap.peakHour.sessionCount
                              }`
                            : "—"}
                        </p>
                      </div>
                      <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/40 p-3">
                        <p className="text-[10px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                          Most used platform
                        </p>
                        <p className="mt-2 text-sm font-semibold text-slate-900 dark:text-white">
                          {recap.mostUsedPlatform
                            ? formatStatsPlatformLabel(recap.mostUsedPlatform.platform)
                            : "—"}
                        </p>
                      </div>
                    </div>
                  </div>

                  <div className="h-full rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                    <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                      Platform mix
                    </p>
                    <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
                      Share of playtime
                    </h2>
                    <div className="mt-5 space-y-4">
                      {platformRows.length === 0 ? (
                        <p className="text-sm text-slate-500 dark:text-slate-400">
                          No platform data available yet.
                        </p>
                      ) : (
                        platformRows.map((item, index) => {
                          const share =
                            platformTotalSeconds > 0
                              ? (item.totalSeconds / platformTotalSeconds) * 100
                              : 0;
                          const barClass =
                            platformBarClasses[index % platformBarClasses.length];
                          const dotClass =
                            platformDotClasses[index % platformDotClasses.length];
                          return (
                            <div key={`platform-${item.platform}`} className="space-y-2">
                              <div className="flex items-center justify-between text-sm">
                                <div className="flex items-center gap-2">
                                  <span className={`h-2.5 w-2.5 rounded-full ${dotClass}`} />
                                  <span className="font-semibold text-slate-900 dark:text-white">
                                    {formatStatsPlatformLabel(item.platform)}
                                  </span>
                                </div>
                                <span className="text-xs font-semibold text-slate-500 dark:text-slate-400">
                                  {Math.round(share)}%
                                </span>
                              </div>
                              <div className="flex items-center justify-between text-xs text-slate-500 dark:text-slate-400">
                                <span>{item.sessionCount} sessions</span>
                                <span className="font-semibold text-emerald-700 dark:text-emerald-200">
                                  {formatHours(item.totalSeconds, 1)}
                                </span>
                              </div>
                              <div className="h-2 w-full overflow-hidden rounded-full bg-slate-200/70 dark:bg-slate-800/70">
                                <div
                                  className={`h-full rounded-full ${barClass}`}
                                  style={{ width: `${share}%` }}
                                />
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
                    <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                      Library mix
                    </p>
                    <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
                      How you explore games
                    </h2>
                    <div className="mt-4 grid gap-3 sm:grid-cols-2">
                      {gameMixStats.map((item) => (
                        <div
                          key={item.label}
                          className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/40 p-3"
                        >
                          <p className="text-[10px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                            {item.label}
                          </p>
                          <p className="mt-2 text-sm font-semibold text-slate-900 dark:text-white">
                            {item.value}
                          </p>
                        </div>
                      ))}
                    </div>
                  </div>

                  <div className="h-full rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
                    <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                      Highlights
                    </p>
                    <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
                      Game leaders
                    </h2>
                    <div className="mt-4 grid gap-3">
                      {gameHighlightStats.map((item) => (
                        <div
                          key={item.label}
                          className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/40 p-3"
                        >
                          <p className="text-[10px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                            {item.label}
                          </p>
                          <p className="mt-2 text-sm font-semibold text-slate-900 dark:text-white">
                            {item.value}
                          </p>
                        </div>
                      ))}
                    </div>
                  </div>
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
                  <div className="mt-5 max-h-96 overflow-auto pr-1">
                    {recap.gamesTable.length === 0 ? (
                      <p className="text-sm text-slate-500 dark:text-slate-400">
                        No games logged yet.
                      </p>
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
                                key={`recap-game-${game.gameId}`}
                                className="border-t border-slate-200/70 dark:border-slate-800/80 bg-slate-200/30 dark:bg-slate-950/40"
                              >
                                <td className="px-3 py-2">
                                  <div className="flex min-w-0 items-center gap-2">
                                    <span className="text-[10px] font-semibold text-slate-500 dark:text-slate-400">
                                      {String(index + 1).padStart(2, "0")}
                                    </span>
                                    <button
                                      type="button"
                                      onClick={() => onNavigate(buildGamePath(game.gameId))}
                                      title={game.title}
                                      className="min-w-0 truncate text-left text-sm font-semibold text-slate-900 dark:text-white transition hover:text-emerald-700 dark:hover:text-emerald-200"
                                    >
                                      {game.title}
                                    </button>
                                  </div>
                                </td>
                                <td className="px-3 py-2 text-slate-700 dark:text-slate-200">
                                  {game.sessionCount}
                                </td>
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
            ) : null}
          </div>
        </section>
      </div>
    </main>
  );
};
