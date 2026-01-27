import { useEffect, useMemo, useState } from "react";

import { apiRequest } from "../api";
import {
  buildGamePath,
  navItems as defaultNavItems,
  platformOptions,
} from "../constants";
import type {
  AccountSessionsResponse,
  AccountStatsPeriod,
  AccountStatsRangeResponse,
  ManualEntryCreateResponse,
  ManualMappingSearchResponse,
  ManualMappingSearchResult,
  NavItem,
} from "../types";
import {
  formatDateLabel,
  formatDuration,
  formatHours,
  formatSessionPlatformLabel,
  formatStatsPlatformLabel,
  formatUtcDateInput,
} from "../utils/formatters";
import { getSessionDurationSeconds } from "../utils/sessions";
import { encodeGameId } from "../utils/gameHash";
import { AppHeader } from "../components/AppHeader";
import { Background } from "../components/Background";
import { ManualEntryModal } from "../components/ManualEntryModal";
import { StatsRecapSection } from "../components/StatsRecapSection";

type SessionsPageProps = {
  path: string;
  navItems?: NavItem[];
  onNavigate: (path: string) => void;
  onLogout: () => void;
};

type DateRangePreset =
  | "today"
  | "yesterday"
  | "this-week"
  | "last-week"
  | "this-month"
  | "this-year"
  | "last-year"
  | "custom";

const getUtcMidnight = (date: Date) =>
  new Date(
    Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate())
  );

const addUtcDays = (date: Date, days: number) =>
  new Date(date.getTime() + days * 24 * 60 * 60 * 1000);

const getPresetRange = (preset: Exclude<DateRangePreset, "custom">) => {
  const today = getUtcMidnight(new Date());
  const year = today.getUTCFullYear();
  const month = today.getUTCMonth();

  if (preset === "today") {
    const value = formatUtcDateInput(today);
    return { from: value, to: value };
  }

  if (preset === "yesterday") {
    const yesterday = addUtcDays(today, -1);
    const value = formatUtcDateInput(yesterday);
    return { from: value, to: value };
  }

  if (preset === "this-week" || preset === "last-week") {
    const weekday = today.getUTCDay();
    const mondayOffset = (weekday + 6) % 7;
    const weekStart = addUtcDays(today, -mondayOffset);
    const start =
      preset === "last-week" ? addUtcDays(weekStart, -7) : weekStart;
    const end = addUtcDays(start, 6);
    return {
      from: formatUtcDateInput(start),
      to: formatUtcDateInput(end),
    };
  }

  if (preset === "this-month") {
    const start = new Date(Date.UTC(year, month, 1));
    const end = new Date(Date.UTC(year, month + 1, 0));
    return {
      from: formatUtcDateInput(start),
      to: formatUtcDateInput(end),
    };
  }

  const targetYear = preset === "last-year" ? year - 1 : year;
  const start = new Date(Date.UTC(targetYear, 0, 1));
  const end = new Date(Date.UTC(targetYear, 11, 31));
  return {
    from: formatUtcDateInput(start),
    to: formatUtcDateInput(end),
  };
};

export const SessionsPage = ({
  path,
  navItems = defaultNavItems,
  onNavigate,
  onLogout,
}: SessionsPageProps) => {
  const defaultRange = getPresetRange("this-week");
  const [sessionRows, setSessionRows] = useState<
    AccountSessionsResponse["sessions"]
  >([]);
  const [sessionsTotal, setSessionsTotal] = useState(0);
  const [sessionsOffset, setSessionsOffset] = useState(0);
  const [isSessionsLoading, setIsSessionsLoading] = useState(false);
  const [sessionsError, setSessionsError] = useState("");
  const [sessionsTitle, setSessionsTitle] = useState("");
  const [sessionsTitleDebounced, setSessionsTitleDebounced] = useState("");
  const [sessionsFrom, setSessionsFrom] = useState(defaultRange.from);
  const [sessionsTo, setSessionsTo] = useState(defaultRange.to);
  const [sessionsSortKey, setSessionsSortKey] = useState<"date" | "duration">(
    "date"
  );
  const [sessionsSortDirection, setSessionsSortDirection] = useState<
    "asc" | "desc"
  >("desc");
  const [rangeStats, setRangeStats] = useState<AccountStatsPeriod | null>(null);
  const [isRangeStatsLoading, setIsRangeStatsLoading] = useState(false);
  const [rangeStatsError, setRangeStatsError] = useState("");
  const [sessionsPreset, setSessionsPreset] =
    useState<DateRangePreset>("this-week");
  const [manualEntrySearchQuery, setManualEntrySearchQuery] = useState("");
  const [manualEntrySearchResults, setManualEntrySearchResults] = useState<
    ManualMappingSearchResult[]
  >([]);
  const [manualEntrySearchError, setManualEntrySearchError] = useState("");
  const [isManualEntrySearchLoading, setIsManualEntrySearchLoading] =
    useState(false);
  const [manualEntrySelection, setManualEntrySelection] =
    useState<ManualMappingSearchResult | null>(null);
  const [manualEntryPlatformKey, setManualEntryPlatformKey] = useState("");
  const [manualEntryError, setManualEntryError] = useState("");
  const [isManualEntrySaving, setIsManualEntrySaving] = useState(false);
  const [manualEntryIgdbAvailable, setManualEntryIgdbAvailable] =
    useState(true);
  const [sessionsReloadToken, setSessionsReloadToken] = useState(0);
  const [isManualEntryModalOpen, setIsManualEntryModalOpen] = useState(false);
  const [manualEntryDate, setManualEntryDate] = useState(() =>
    new Date().toISOString().slice(0, 10)
  );
  const [manualEntryHours, setManualEntryHours] = useState("1");
  const [manualEntryMinutes, setManualEntryMinutes] = useState("0");
  const [deletingSessionIds, setDeletingSessionIds] = useState<number[]>([]);

  const sessionsLimit = 10;

  const refreshSessions = () => {
    setSessionsReloadToken((current) => current + 1);
  };

  const handlePresetSelect = (preset: Exclude<DateRangePreset, "custom">) => {
    const range = getPresetRange(preset);
    setSessionsFrom(range.from);
    setSessionsTo(range.to);
    setSessionsPreset(preset);
  };

  const openManualEntryModal = () => {
    setManualEntryDate(new Date().toISOString().slice(0, 10));
    setIsManualEntryModalOpen(true);
  };

  const closeManualEntryModal = () => {
    setIsManualEntryModalOpen(false);
    setManualEntrySearchQuery("");
    setManualEntrySearchResults([]);
    setManualEntrySearchError("");
    setManualEntrySelection(null);
    setManualEntryPlatformKey("");
    setManualEntryError("");
    setManualEntryHours("1");
    setManualEntryMinutes("0");
  };

  const handleManualEntrySearch = async () => {
    const query = manualEntrySearchQuery.trim();
    if (!query) {
      setManualEntrySearchError("Enter a search query.");
      return;
    }

    setIsManualEntrySearchLoading(true);
    setManualEntrySearchError("");
    try {
      const response = await apiRequest<ManualMappingSearchResponse>(
        `/manual/search?query=${encodeURIComponent(query)}`
      );
      setManualEntryIgdbAvailable(response.igdbAvailable);
      setManualEntrySearchResults(response.results);
    } catch (err) {
      setManualEntrySearchResults([]);
      setManualEntrySearchError(
        err instanceof Error ? err.message : "Failed to search games."
      );
    } finally {
      setIsManualEntrySearchLoading(false);
    }
  };

  const handleManualEntryCreate = async () => {
    setManualEntryError("");
    const hours = Number(manualEntryHours);
    const minutes = Number(manualEntryMinutes);
    if (
      !Number.isFinite(hours) ||
      !Number.isFinite(minutes) ||
      hours < 0 ||
      minutes < 0
    ) {
      setManualEntryError("Duration must be a positive number.");
      return;
    }

    const durationMinutes = Math.floor(hours * 60 + minutes);
    if (durationMinutes <= 0) {
      setManualEntryError("Duration must be at least 1 minute.");
      return;
    }

    if (!manualEntryDate) {
      setManualEntryError("Select a date for this entry.");
      return;
    }

    if (!manualEntrySelection) {
      setManualEntryError("Select a game to log.");
      return;
    }

    if (
      manualEntrySelection.source === "igdb" &&
      !manualEntryPlatformKey.trim()
    ) {
      setManualEntryError("Select a platform for the IGDB result.");
      return;
    }

    setIsManualEntrySaving(true);
    try {
      if (manualEntrySelection.source === "local") {
        if (!manualEntrySelection.gameId) {
          setManualEntryError("Select a saved game.");
          return;
        }
        await apiRequest<ManualEntryCreateResponse>("/manual/entries", {
          method: "POST",
          body: {
            date: manualEntryDate,
            durationMinutes,
            gameId: encodeGameId(manualEntrySelection.gameId),
          },
        });
      } else {
        if (!manualEntrySelection.igdbId) {
          setManualEntryError("Select an IGDB game.");
          return;
        }
        await apiRequest<ManualEntryCreateResponse>("/manual/entries", {
          method: "POST",
          body: {
            date: manualEntryDate,
            durationMinutes,
            igdbId: manualEntrySelection.igdbId,
            title: manualEntrySelection.title,
            platformKey: manualEntryPlatformKey,
            capsuleUrl: manualEntrySelection.capsuleUrl,
          },
        });
      }

      closeManualEntryModal();
      refreshSessions();
    } catch (err) {
      setManualEntryError(
        err instanceof Error ? err.message : "Failed to create entry."
      );
    } finally {
      setIsManualEntrySaving(false);
    }
  };

  const handleSessionRemove = async (sessionId: number) => {
    if (!window.confirm("Remove this session entry?")) {
      return;
    }
    setDeletingSessionIds((current) => [...current, sessionId]);
    setSessionsError("");
    try {
      await apiRequest(`/sessions/${sessionId}`, { method: "DELETE" });
      refreshSessions();
    } catch (err) {
      setSessionsError(
        err instanceof Error ? err.message : "Failed to remove session."
      );
    } finally {
      setDeletingSessionIds((current) =>
        current.filter((id) => id !== sessionId)
      );
    }
  };

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setSessionsTitleDebounced(sessionsTitle.trim());
    }, 300);

    return () => window.clearTimeout(timer);
  }, [sessionsTitle]);

  useEffect(() => {
    setSessionsOffset(0);
  }, [sessionsFrom, sessionsTo]);

  useEffect(() => {
    setSessionsOffset(0);
  }, [sessionsTitleDebounced]);

  useEffect(() => {
    let cancelled = false;
    setIsSessionsLoading(true);
    setSessionsError("");

    const loadSessions = async () => {
      try {
        const params = new URLSearchParams({
          limit: String(sessionsLimit),
          offset: String(sessionsOffset),
        });

        if (sessionsTitleDebounced) {
          params.set("title", sessionsTitleDebounced);
        }

        if (sessionsFrom) {
          params.set("from", sessionsFrom);
        }

        if (sessionsTo) {
          params.set("to", sessionsTo);
        }

        params.set("sort", sessionsSortKey);
        params.set("direction", sessionsSortDirection);

        const data = await apiRequest<AccountSessionsResponse>(
          `/sessions?${params.toString()}`
        );
        if (cancelled) {
          return;
        }
        setSessionRows(data.sessions);
        setSessionsTotal(data.total);
        setIsSessionsLoading(false);
      } catch (err) {
        if (!cancelled) {
          setSessionRows([]);
          setSessionsTotal(0);
          setIsSessionsLoading(false);
          setSessionsError(
            err instanceof Error ? err.message : "Failed to load sessions."
          );
        }
      }
    };

    loadSessions();
    return () => {
      cancelled = true;
    };
  }, [
    sessionsOffset,
    sessionsLimit,
    sessionsTitleDebounced,
    sessionsFrom,
    sessionsTo,
    sessionsSortKey,
    sessionsSortDirection,
    sessionsReloadToken,
  ]);

  useEffect(() => {
    if (!sessionsFrom || !sessionsTo) {
      setRangeStats(null);
      return;
    }

    let cancelled = false;
    setIsRangeStatsLoading(true);
    setRangeStatsError("");

    const loadStats = async () => {
      try {
        const params = new URLSearchParams({
          from: sessionsFrom,
          to: sessionsTo,
        });
        const data = await apiRequest<AccountStatsRangeResponse>(
          `/stats?${params.toString()}`
        );
        if (cancelled) {
          return;
        }
        setRangeStats(data.stats);
        setIsRangeStatsLoading(false);
      } catch (err) {
        if (cancelled) {
          return;
        }
        setRangeStats(null);
        setIsRangeStatsLoading(false);
        setRangeStatsError(
          err instanceof Error ? err.message : "Failed to load session stats."
        );
      }
    };

    void loadStats();
    return () => {
      cancelled = true;
    };
  }, [sessionsFrom, sessionsTo, sessionsReloadToken]);

  const hasSessionFilters =
    Boolean(sessionsTitle.trim()) ||
    Boolean(sessionsFrom) ||
    Boolean(sessionsTo);
  const sessionsStart = sessionsTotal === 0 ? 0 : sessionsOffset + 1;
  const sessionsEnd = Math.min(sessionsOffset + sessionsLimit, sessionsTotal);
  const totalPages = Math.max(1, Math.ceil(sessionsTotal / sessionsLimit));
  const currentPage = Math.min(
    totalPages,
    Math.floor(sessionsOffset / sessionsLimit) + 1
  );
  const maxPages = 4;
  const startPage = Math.max(
    1,
    Math.min(currentPage - 1, totalPages - maxPages + 1)
  );
  const pageNumbers = Array.from(
    { length: Math.min(totalPages, maxPages) },
    (_, index) => startPage + index
  );

  const handleSessionSort = (key: "date" | "duration") => {
    if (sessionsSortKey === key) {
      setSessionsSortDirection((current) =>
        current === "asc" ? "desc" : "asc"
      );
      setSessionsOffset(0);
      return;
    }
    setSessionsSortKey(key);
    setSessionsSortDirection("desc");
    setSessionsOffset(0);
  };

  const renderSessionSortIcon = (key: "date" | "duration") => {
    const isActive = sessionsSortKey === key;
    const direction = sessionsSortDirection;
    return (
      <svg
        aria-hidden="true"
        viewBox="0 0 20 20"
        className={`h-3 w-3 ${isActive ? "text-emerald-200" : ""}`}
        fill="currentColor"
      >
        {isActive ? (
          direction === "asc" ? (
            <path d="M10 4l4 4H6l4-4z" />
          ) : (
            <path d="M10 16l-4-4h8l-4 4z" />
          )
        ) : (
          <path d="M6 8l4-4 4 4H6zM14 12l-4 4-4-4h8z" />
        )}
      </svg>
    );
  };

  const renderIgnoredIcon = () => (
    <span
      className="inline-flex h-5 w-5 items-center justify-center text-slate-400"
      title="Ignored from stats"
      aria-label="Ignored from stats"
    >
      <svg
        aria-hidden="true"
        viewBox="0 0 24 24"
        className="h-4 w-4"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.5"
      >
        <circle cx="12" cy="12" r="8" />
        <path strokeLinecap="round" d="M8 8l8 8" />
      </svg>
    </span>
  );

  const sessionRowsView = useMemo(
    () =>
      sessionRows.map((session) => ({
        sessionId: session.sessionId,
        gameId: session.gameId,
        title: session.game,
        platform: formatSessionPlatformLabel(session),
        date: formatDateLabel(new Date(session.startedAt)),
        duration: formatDuration(getSessionDurationSeconds(session), "short"),
        imageUrl: session.capsuleUrl ?? null,
        ignored: session.ignored,
      })),
    [sessionRows]
  );

  const sessionPresets: {
    key: Exclude<DateRangePreset, "custom">;
    label: string;
  }[] = [
    { key: "today", label: "Today" },
    { key: "yesterday", label: "Yesterday" },
    { key: "this-week", label: "This week" },
    { key: "last-week", label: "Last week" },
    { key: "this-month", label: "This month" },
    { key: "this-year", label: "This year" },
    { key: "last-year", label: "Last year" },
  ];

  const handleGameSelect = (gameId: string) => {
    onNavigate(buildGamePath(gameId));
  };

  const platformPieColors = ["#34d399", "#22d3ee", "#a78bfa", "#f59e0b"];
  const statsRangeLabel = useMemo(() => {
    if (!sessionsFrom && !sessionsTo) {
      return "All time";
    }
    if (sessionsFrom && sessionsTo) {
      const fromLabel = formatDateLabel(new Date(`${sessionsFrom}T00:00:00Z`));
      const toLabel = formatDateLabel(new Date(`${sessionsTo}T00:00:00Z`));
      return `${fromLabel} to ${toLabel}`;
    }
    if (sessionsFrom) {
      return `From ${formatDateLabel(new Date(`${sessionsFrom}T00:00:00Z`))}`;
    }
    return `Until ${formatDateLabel(new Date(`${sessionsTo}T00:00:00Z`))}`;
  }, [sessionsFrom, sessionsTo]);

  const rangePlatforms = useMemo(() => {
    if (!rangeStats) {
      return [];
    }
    const total = Math.max(rangeStats.totalSeconds, 1);
    return rangeStats.platforms.map((item) => ({
      label: formatStatsPlatformLabel(item.platform),
      value: Math.round((item.totalSeconds / total) * 100),
      hours: formatHours(item.totalSeconds, 1),
    }));
  }, [rangeStats]);
  const rangeTopGames = useMemo(() => {
    if (!rangeStats) {
      return [];
    }
    return rangeStats.topGames.map((game) => ({
      gameId: game.gameId,
      title: game.title,
      hours: formatHours(game.totalSeconds, 1),
    }));
  }, [rangeStats]);
  const rangeSummary = useMemo(() => {
    if (!rangeStats) {
      return {
        hours: "0h",
        sessions: "0",
        topTitle: "—",
      };
    }
    return {
      hours: formatHours(rangeStats.totalSeconds, 1),
      sessions: String(rangeStats.sessionCount),
      topTitle: rangeStats.topGameTitle ?? "—",
    };
  }, [rangeStats]);

  return (
    <main className="relative min-h-screen overflow-hidden bg-slate-950 text-slate-100">
      <Background />
      <div className="relative mx-auto w-full max-w-6xl p-3 sm:px-6 sm:py-12">
        <AppHeader
          title="Sessions"
          navItems={navItems}
          path={path}
          onNavigate={onNavigate}
          onLogout={onLogout}
        />

        <section className="mt-10 rounded-3xl border border-slate-800 bg-slate-900/60 p-5 sm:p-6">
          <div className="flex flex-wrap items-center justify-between gap-4">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
                Sessions log
              </p>
              <h2 className="mt-2 text-xl font-semibold text-white">
                Every session tracked
              </h2>
            </div>
            <button
              type="button"
              onClick={openManualEntryModal}
              className="rounded-full border border-emerald-400/60 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-emerald-200 transition hover:border-emerald-300"
            >
              Add entry
            </button>
          </div>

          <div className="mt-6 flex flex-wrap gap-2">
            {sessionPresets.map((preset) => (
              <button
                key={preset.key}
                type="button"
                onClick={() => handlePresetSelect(preset.key)}
                className={`rounded-full border px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.3em] transition ${
                  sessionsPreset === preset.key
                    ? "border-emerald-300 bg-emerald-400 text-slate-900"
                    : "border-slate-700 text-slate-200 hover:border-emerald-300/70"
                }`}
              >
                {preset.label}
              </button>
            ))}
          </div>

          <div className="mt-6 grid gap-3 md:grid-cols-[1.2fr_0.6fr_0.6fr]">
            <label className="text-xs font-medium text-slate-300">
              Title
              <input
                type="text"
                placeholder="Search by game title"
                value={sessionsTitle}
                onChange={(event) => setSessionsTitle(event.target.value)}
                className="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950/80 px-3 py-2 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
              />
            </label>
            <label className="text-xs font-medium text-slate-300">
              From
              <input
                type="date"
                value={sessionsFrom}
                onChange={(event) => {
                  setSessionsFrom(event.target.value);
                  setSessionsPreset("custom");
                }}
                className="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950/80 px-3 py-2 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
              />
            </label>
            <label className="text-xs font-medium text-slate-300">
              To
              <input
                type="date"
                value={sessionsTo}
                onChange={(event) => {
                  setSessionsTo(event.target.value);
                  setSessionsPreset("custom");
                }}
                className="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950/80 px-3 py-2 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
              />
            </label>
          </div>
          {sessionsError ? (
            <p className="mt-4 text-xs text-rose-300">{sessionsError}</p>
          ) : null}
          {rangeStatsError ? (
            <p className="mt-4 text-xs text-rose-300">{rangeStatsError}</p>
          ) : null}

          <div className="mt-6">
            <StatsRecapSection
              title="Sessions recap"
              subtitle={statsRangeLabel}
              summary={[
                { label: "Hours played", value: rangeSummary.hours },
                { label: "Sessions", value: rangeSummary.sessions },
                { label: "Top title", value: rangeSummary.topTitle },
              ]}
              platforms={rangePlatforms}
              topGames={rangeTopGames}
              colors={platformPieColors}
              onGameSelect={handleGameSelect}
            />
            {isRangeStatsLoading ? (
              <p className="mt-3 text-xs text-slate-400">Loading stats...</p>
            ) : null}
          </div>

          <div className="mt-6 space-y-3 md:hidden">
            <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-slate-800 bg-slate-950/60 px-4 py-3 text-[11px] uppercase tracking-[0.25em] text-slate-400">
              <span>Sort</span>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => handleSessionSort("date")}
                  className="inline-flex items-center gap-2 rounded-full border border-slate-700 px-3 py-1 font-semibold transition hover:border-emerald-400/60 hover:text-emerald-100"
                >
                  Date
                  {renderSessionSortIcon("date")}
                </button>
                <button
                  type="button"
                  onClick={() => handleSessionSort("duration")}
                  className="inline-flex items-center gap-2 rounded-full border border-slate-700 px-3 py-1 font-semibold transition hover:border-emerald-400/60 hover:text-emerald-100"
                >
                  Duration
                  {renderSessionSortIcon("duration")}
                </button>
              </div>
            </div>

            <div className="grid gap-3">
              {isSessionsLoading ? (
                [1, 2, 3, 4].map((row) => (
                  <div
                    key={row}
                    className="rounded-2xl border border-slate-800 bg-slate-950/60 p-4"
                  >
                    <div className="flex items-start gap-3">
                      <div className="h-14 w-10 rounded-xl bg-slate-800/60" />
                      <div className="flex-1 space-y-2">
                        <div className="h-3 w-32 rounded-full bg-slate-800/60" />
                        <div className="h-3 w-20 rounded-full bg-slate-800/50" />
                        <div className="h-3 w-16 rounded-full bg-slate-800/50" />
                      </div>
                      <div className="h-3 w-12 rounded-full bg-slate-800/50" />
                    </div>
                    <div className="mt-3 h-8 w-20 rounded-full bg-slate-800/50" />
                  </div>
                ))
              ) : sessionRowsView.length === 0 ? (
                <div className="rounded-2xl border border-dashed border-slate-800 bg-slate-950/50 p-4 text-sm text-slate-400">
                  {hasSessionFilters
                    ? "No sessions match your filters."
                    : "No sessions yet. Sync a provider to populate this list."}
                </div>
              ) : (
                sessionRowsView.map((session) => (
                  <div
                    key={session.sessionId}
                    className="rounded-2xl border border-slate-800 bg-slate-950/60 p-4"
                  >
                    <div className="flex items-start gap-3">
                      {session.imageUrl ? (
                        <img
                          src={session.imageUrl}
                          alt={`${session.title} capsule art`}
                          className="h-14 w-10 rounded-xl border border-slate-800 object-cover"
                        />
                      ) : (
                        <div className="h-14 w-10 rounded-xl border border-slate-800 bg-slate-800/60" />
                      )}
                      <div className="flex-1 space-y-1">
                        <button
                          type="button"
                          onClick={() => handleGameSelect(session.gameId)}
                          className="text-left font-semibold text-white transition hover:text-emerald-200"
                    >
                      {session.title}
                    </button>
                    <p className="text-xs text-slate-400">
                      {session.platform}
                    </p>
                    <p className="text-xs text-slate-400">
                      {session.date}
                    </p>
                  </div>
                  <div className="flex items-center gap-2">
                    {session.ignored ? renderIgnoredIcon() : null}
                    <span className="text-sm font-semibold text-emerald-200">
                      {session.duration}
                    </span>
                  </div>
                </div>
                <div className="mt-3 flex items-center justify-between">
                  <span className="text-[11px] uppercase tracking-[0.3em] text-slate-500">
                    {session.platform}
                  </span>
                  <button
                    type="button"
                    onClick={() => handleSessionRemove(session.sessionId)}
                    disabled={deletingSessionIds.includes(
                      session.sessionId
                        )}
                        className="rounded-full border border-rose-500/60 px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.3em] text-rose-200 transition hover:border-rose-400 disabled:cursor-not-allowed disabled:opacity-60"
                      >
                        Remove
                      </button>
                    </div>
                  </div>
                ))
              )}
            </div>
          </div>

          <div className="mt-6 hidden overflow-hidden rounded-2xl border border-slate-800 md:block">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-950/70 text-xs uppercase tracking-[0.3em] text-slate-400">
                <tr>
                  <th className="px-4 py-3 text-left">Title</th>
                  <th className="px-4 py-3 text-left">Platform</th>
                  <th className="px-4 py-3 text-left">
                    <button
                      type="button"
                      onClick={() => handleSessionSort("date")}
                      className="inline-flex items-center gap-2 text-xs uppercase tracking-[0.3em] text-slate-400 transition hover:text-slate-200"
                    >
                      Date
                      {renderSessionSortIcon("date")}
                    </button>
                  </th>
                  <th className="px-4 py-3 text-right">
                    <button
                      type="button"
                      onClick={() => handleSessionSort("duration")}
                      className="inline-flex items-center gap-2 text-xs uppercase tracking-[0.3em] text-slate-400 transition hover:text-slate-200"
                    >
                      Duration
                      {renderSessionSortIcon("duration")}
                    </button>
                  </th>
                  <th className="px-4 py-3 text-right">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800 bg-slate-900/60">
                {isSessionsLoading ? (
                  [1, 2, 3, 4].map((row) => (
                    <tr key={row}>
                      <td className="px-4 py-4">
                        <div className="flex items-center gap-3">
                          <div className="h-12 w-9 rounded-lg bg-slate-800/60" />
                          <div className="h-3 w-28 rounded-full bg-slate-800/60" />
                        </div>
                      </td>
                      <td className="px-4 py-4">
                        <div className="h-3 w-16 rounded-full bg-slate-800/50" />
                      </td>
                      <td className="px-4 py-4">
                        <div className="h-3 w-12 rounded-full bg-slate-800/50" />
                      </td>
                      <td className="px-4 py-4">
                        <div className="ml-auto h-3 w-12 rounded-full bg-slate-800/50" />
                      </td>
                      <td className="px-4 py-4">
                        <div className="ml-auto h-3 w-16 rounded-full bg-slate-800/40" />
                      </td>
                    </tr>
                  ))
                ) : sessionRowsView.length === 0 ? (
                  <tr>
                    <td
                      colSpan={5}
                      className="px-4 py-10 text-center text-sm text-slate-400"
                    >
                      {hasSessionFilters
                        ? "No sessions match your filters."
                        : "No sessions yet. Sync a provider to populate this list."}
                    </td>
                  </tr>
                ) : (
                  sessionRowsView.map((session) => (
                    <tr
                      key={`${session.sessionId}`}
                      className="transition hover:bg-slate-900/70"
                    >
                      <td className="px-4 py-3">
                        <div className="flex items-center gap-3">
                          {session.imageUrl ? (
                            <img
                              src={session.imageUrl}
                              alt={`${session.title} capsule art`}
                              className="h-12 w-9 rounded-lg border border-slate-800 object-cover"
                            />
                          ) : (
                            <div className="h-12 w-9 rounded-lg border border-slate-800 bg-slate-800/60" />
                          )}
                          <button
                            type="button"
                            onClick={() => handleGameSelect(session.gameId)}
                            className="text-left font-semibold text-white transition hover:text-emerald-200"
                          >
                            {session.title}
                          </button>
                        </div>
                      </td>
                      <td className="px-4 py-3 text-slate-300">
                        {session.platform}
                      </td>
                      <td className="px-4 py-3 text-slate-300">
                        {session.date}
                      </td>
                      <td className="px-4 py-3 text-right text-emerald-200">
                        <div className="inline-flex items-center justify-end gap-2">
                          {session.ignored ? renderIgnoredIcon() : null}
                          <span>{session.duration}</span>
                        </div>
                      </td>
                      <td className="px-4 py-3 text-right">
                        <button
                          type="button"
                          onClick={() => handleSessionRemove(session.sessionId)}
                          disabled={deletingSessionIds.includes(
                            session.sessionId
                          )}
                          className="rounded-full border border-rose-500/60 px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.3em] text-rose-200 transition hover:border-rose-400 disabled:cursor-not-allowed disabled:opacity-60"
                        >
                          Remove
                        </button>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>

          <div className="mt-6 flex flex-wrap items-center justify-between gap-4 text-xs text-slate-300">
            <p>
              Showing {sessionsStart}-{sessionsEnd} of {sessionsTotal} sessions
            </p>
            <div className="flex items-center gap-2">
              <button
                type="button"
                disabled={sessionsOffset === 0 || isSessionsLoading}
                onClick={() =>
                  setSessionsOffset((current) =>
                    Math.max(0, current - sessionsLimit)
                  )
                }
                className="rounded-full border border-slate-700 px-3 py-2 font-semibold uppercase tracking-[0.3em] text-slate-200"
              >
                Prev
              </button>
              {pageNumbers.map((page) => (
                <button
                  key={page}
                  type="button"
                  onClick={() => setSessionsOffset((page - 1) * sessionsLimit)}
                  className={`flex h-9 w-9 items-center justify-center rounded-full text-xs font-semibold leading-none ${
                    page === currentPage
                      ? "bg-emerald-400 text-slate-900"
                      : "border border-slate-700 text-slate-200"
                  }`}
                >
                  {page}
                </button>
              ))}
              <button
                type="button"
                disabled={
                  sessionsOffset + sessionsLimit >= sessionsTotal ||
                  isSessionsLoading
                }
                onClick={() =>
                  setSessionsOffset((current) => current + sessionsLimit)
                }
                className="rounded-full border border-slate-700 px-3 py-2 font-semibold uppercase tracking-[0.3em] text-slate-200"
              >
                Next
              </button>
            </div>
          </div>
        </section>
      </div>

      <ManualEntryModal
        isOpen={isManualEntryModalOpen}
        manualEntryDate={manualEntryDate}
        manualEntryHours={manualEntryHours}
        manualEntryMinutes={manualEntryMinutes}
        manualEntrySearchQuery={manualEntrySearchQuery}
        manualEntrySearchResults={manualEntrySearchResults}
        manualEntrySelection={manualEntrySelection}
        manualEntryPlatformKey={manualEntryPlatformKey}
        manualEntryError={manualEntryError}
        manualEntrySearchError={manualEntrySearchError}
        manualEntryIgdbAvailable={manualEntryIgdbAvailable}
        isManualEntrySearchLoading={isManualEntrySearchLoading}
        isManualEntrySaving={isManualEntrySaving}
        platformOptions={platformOptions}
        onClose={closeManualEntryModal}
        onSearch={handleManualEntrySearch}
        onSave={handleManualEntryCreate}
        onDateChange={setManualEntryDate}
        onHoursChange={setManualEntryHours}
        onMinutesChange={setManualEntryMinutes}
        onSearchQueryChange={setManualEntrySearchQuery}
        onSelectResult={(result) => {
          setManualEntrySelection(result);
          setManualEntryPlatformKey("");
          setManualEntryError("");
        }}
        onPlatformKeyChange={setManualEntryPlatformKey}
      />
    </main>
  );
};
