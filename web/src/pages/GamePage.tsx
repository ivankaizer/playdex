import { useEffect, useMemo, useState } from "react";

import { apiRequest } from "../api";
import { capsuleUrl, navItems as defaultNavItems } from "../constants";
import type {
  AccountSession,
  AccountSessionsResponse,
  GameIgnoreResponse,
  GameRatingResponse,
  NavItem,
} from "../types";
import {
  formatDateLabel,
  formatDuration,
  formatHours,
  formatSessionPlatformLabel,
  formatTimeLabel,
  formatUtcDateInput,
} from "../utils/formatters";
import { getSessionDurationSeconds } from "../utils/sessions";
import { AppHeader } from "../components/AppHeader";
import { Background } from "../components/Background";

type GamePageProps = {
  path: string;
  navItems?: NavItem[];
  gameTitle: string;
  onNavigate: (path: string) => void;
  onLogout: () => void;
};

const ALL_TIME_START_YEAR = 2025;

const formatYearLabel = (year: number) => String(year);
const formatMonthLabel = (date: Date) =>
  new Intl.DateTimeFormat("en-US", { month: "short" }).format(date);
const ratingLabels = [
  "",
  "Rough",
  "Okay",
  "Good",
  "Great",
  "Legendary",
];

const RatingStar = ({ filled }: { filled: boolean }) => (
  <svg
    aria-hidden="true"
    viewBox="0 0 24 24"
    className={`h-6 w-6 drop-shadow ${filled ? "text-amber-300" : "text-slate-600"}`}
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
);

export const GamePage = ({
  path,
  navItems = defaultNavItems,
  gameTitle,
  onNavigate,
  onLogout,
}: GamePageProps) => {
  const [recentSessions, setRecentSessions] = useState<AccountSession[]>([]);
  const [allSessions, setAllSessions] = useState<AccountSession[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [gameError, setGameError] = useState("");
  const [isIgnoreChecking, setIsIgnoreChecking] = useState(false);
  const [isIgnoreSaving, setIsIgnoreSaving] = useState(false);
  const [isIgnored, setIsIgnored] = useState(false);
  const [ignoreError, setIgnoreError] = useState("");
  const [rating, setRating] = useState<number | null>(null);
  const [isRatingLoading, setIsRatingLoading] = useState(false);
  const [isRatingSaving, setIsRatingSaving] = useState(false);
  const [ratingError, setRatingError] = useState("");
  const [sessionsLoaded, setSessionsLoaded] = useState(false);
  const [selectedYear, setSelectedYear] = useState(
    new Date().getUTCFullYear()
  );
  const [recentPage, setRecentPage] = useState(1);
  const recentPageSize = 5;
  const currentGameId = useMemo(
    () => allSessions[0]?.gameId ?? null,
    [allSessions]
  );

  useEffect(() => {
    if (!gameTitle) {
      return;
    }
    let isCancelled = false;

    const loadGameSessions = async () => {
      setIsLoading(true);
      setGameError("");
      setSessionsLoaded(false);
      setRating(null);
      setRatingError("");
      setIsRatingLoading(true);
      try {
        const now = new Date();
        const from = new Date(Date.UTC(ALL_TIME_START_YEAR, 0, 1));
        const fromDate = formatUtcDateInput(from);
        const toDate = formatUtcDateInput(now);
        const baseParams = new URLSearchParams({
          title: gameTitle,
          from: fromDate,
          to: toDate,
          sort: "date",
          direction: "desc",
        });

        const firstParams = new URLSearchParams(baseParams);
        firstParams.set("limit", String(recentPageSize));
        firstParams.set("offset", "0");
        const firstResponse = await apiRequest<AccountSessionsResponse>(
          `/sessions?${firstParams.toString()}`,
          { onAuthFailure: onLogout }
        );

        if (isCancelled) {
          return;
        }

        const allSessions = [...firstResponse.sessions];
        let offset = firstResponse.limit;
        const total = firstResponse.total;

        while (offset < total) {
          const pageParams = new URLSearchParams(baseParams);
          pageParams.set("limit", "100");
          pageParams.set("offset", String(offset));
          const response = await apiRequest<AccountSessionsResponse>(
            `/sessions?${pageParams.toString()}`,
            { onAuthFailure: onLogout }
          );

          if (isCancelled) {
            return;
          }

          allSessions.push(...response.sessions);
          offset += response.limit;
          if (response.sessions.length === 0) {
            break;
          }
        }

        if (isCancelled) {
          return;
        }

        setRecentSessions(allSessions);
        setAllSessions(allSessions);
        setRecentPage(1);
        setIsLoading(false);
        setSessionsLoaded(true);
      } catch (err) {
        if (isCancelled) {
          return;
        }
        setRecentSessions([]);
        setAllSessions([]);
        setIsLoading(false);
        setSessionsLoaded(true);
        setGameError(
          err instanceof Error ? err.message : "Failed to load game sessions."
        );
      }
    };

    void loadGameSessions();
    return () => {
      isCancelled = true;
    };
  }, [gameTitle, onLogout]);

  useEffect(() => {
    if (!sessionsLoaded) {
      setIsIgnoreChecking(true);
      setIgnoreError("");
      return;
    }

    if (!currentGameId) {
      setIsIgnoreChecking(false);
      setIsIgnored(false);
      setIgnoreError(
        allSessions.length === 0
          ? "No sessions found yet to identify this game."
          : "Unable to resolve game id for this game."
      );
      return;
    }

    let isCancelled = false;

    const loadIgnoreStatus = async () => {
      setIsIgnoreChecking(true);
      setIsIgnored(false);
      setIgnoreError("");
      try {
        const params = new URLSearchParams({ gameId: String(currentGameId) });
        const response = await apiRequest<GameIgnoreResponse>(
          `/games/ignored?${params.toString()}`,
          { onAuthFailure: onLogout }
        );

        if (isCancelled) {
          return;
        }

        setIsIgnored(response.ignored);
        setIgnoreError("");
      } catch (err) {
        if (isCancelled) {
          return;
        }
        setIsIgnored(false);
        setIgnoreError(
          err instanceof Error
            ? err.message
            : "Failed to load ignore status."
        );
      } finally {
        if (!isCancelled) {
          setIsIgnoreChecking(false);
        }
      }
    };

    void loadIgnoreStatus();
    return () => {
      isCancelled = true;
    };
  }, [allSessions.length, currentGameId, onLogout, sessionsLoaded]);

  useEffect(() => {
    if (!sessionsLoaded) {
      setIsRatingLoading(true);
      setRatingError("");
      return;
    }

    if (!currentGameId) {
      setIsRatingLoading(false);
      setRating(null);
      setRatingError(
        allSessions.length === 0
          ? "Log a session to start rating this game."
          : "Unable to resolve game id for this game."
      );
      return;
    }

    let isCancelled = false;

    const loadRating = async () => {
      setIsRatingLoading(true);
      setRatingError("");
      try {
        const params = new URLSearchParams({ gameId: String(currentGameId) });
        const response = await apiRequest<GameRatingResponse>(
          `/games/rating?${params.toString()}`,
          { onAuthFailure: onLogout }
        );

        if (isCancelled) {
          return;
        }

        setRating(response.rating);
      } catch (err) {
        if (isCancelled) {
          return;
        }
        setRating(null);
        setRatingError(
          err instanceof Error ? err.message : "Failed to load rating."
        );
      } finally {
        if (!isCancelled) {
          setIsRatingLoading(false);
        }
      }
    };

    void loadRating();
    return () => {
      isCancelled = true;
    };
  }, [allSessions.length, currentGameId, onLogout, sessionsLoaded]);

  const handleToggleIgnore = async () => {
    if (!currentGameId) {
      setIgnoreError("Cannot toggle without a game id.");
      return;
    }
    setIsIgnoreSaving(true);
    setIgnoreError("");
    try {
      if (isIgnored) {
        const response = await apiRequest<GameIgnoreResponse>(
          "/games/ignored",
          {
            method: "DELETE",
            body: { gameId: currentGameId },
            onAuthFailure: onLogout,
          }
        );
        setIsIgnored(response.ignored);
      } else {
        const response = await apiRequest<GameIgnoreResponse>(
          "/games/ignored",
          {
            method: "POST",
            body: { gameId: currentGameId },
            onAuthFailure: onLogout,
          }
        );
        setIsIgnored(response.ignored);
      }
    } catch (err) {
      setIgnoreError(
        err instanceof Error
          ? err.message
          : "Failed to update ignore status."
      );
    } finally {
      setIsIgnoreSaving(false);
    }
  };

  const handleRateGame = async (value: number | null) => {
    if (!currentGameId) {
      setRatingError("Cannot rate without a game id.");
      return;
    }
    setIsRatingSaving(true);
    setRatingError("");
    try {
      if (value === null) {
        const response = await apiRequest<GameRatingResponse>(
          "/games/rating",
          {
            method: "DELETE",
            body: { gameId: currentGameId },
            onAuthFailure: onLogout,
          }
        );
        setRating(response.rating);
      } else {
        const response = await apiRequest<GameRatingResponse>(
          "/games/rating",
          {
            method: "POST",
            body: { gameId: currentGameId, rating: value },
            onAuthFailure: onLogout,
          }
        );
        setRating(response.rating);
      }
    } catch (err) {
      setRatingError(
        err instanceof Error
          ? err.message
          : "Failed to update rating."
      );
    } finally {
      setIsRatingSaving(false);
    }
  };

  const gameImage =
    recentSessions.find((session) => session.capsuleUrl)?.capsuleUrl ??
    capsuleUrl;

  const totalSeconds = useMemo(
    () =>
      allSessions.reduce(
        (sum, session) => sum + getSessionDurationSeconds(session),
        0
      ),
    [allSessions]
  );

  const lastPlayed = recentSessions[0]
    ? new Date(recentSessions[0].startedAt)
    : null;

  const yearlyStats = useMemo(() => {
    const now = new Date();
    const endYear = now.getUTCFullYear();
    const buckets = Array.from(
      { length: endYear - ALL_TIME_START_YEAR + 1 },
      (_, index) => {
        const year = ALL_TIME_START_YEAR + index;
        const key = String(year);
        return {
          key,
          label: formatYearLabel(year),
          year,
          seconds: 0,
        };
      }
    );

    const bucketMap = new Map(buckets.map((item) => [item.key, item]));
    allSessions.forEach((session) => {
      const date = new Date(session.startedAt);
      const year = date.getUTCFullYear();
      if (year < ALL_TIME_START_YEAR) {
        return;
      }
      const key = String(year);
      const bucket = bucketMap.get(key);
      if (bucket) {
        bucket.seconds += getSessionDurationSeconds(session);
      }
    });

    return buckets.map((bucket) => ({
      ...bucket,
      hours: bucket.seconds / 3600,
    }));
  }, [allSessions]);

  const maxHours = Math.max(...yearlyStats.map((item) => item.hours), 1);
  const availableYears = useMemo(
    () => yearlyStats.map((item) => item.year),
    [yearlyStats]
  );
  const activeYear = availableYears.includes(selectedYear)
    ? selectedYear
    : availableYears[availableYears.length - 1] ?? selectedYear;

  const monthlyStats = useMemo(() => {
    const buckets = Array.from({ length: 12 }, (_, index) => {
      const date = new Date(Date.UTC(activeYear, index, 1));
      const key = `${activeYear}-${index}`;
      return {
        key,
        label: formatMonthLabel(date),
        month: index,
        seconds: 0,
      };
    });

    const bucketMap = new Map(buckets.map((item) => [item.key, item]));
    allSessions.forEach((session) => {
      const date = new Date(session.startedAt);
      if (date.getUTCFullYear() !== activeYear) {
        return;
      }
      const key = `${date.getUTCFullYear()}-${date.getUTCMonth()}`;
      const bucket = bucketMap.get(key);
      if (bucket) {
        bucket.seconds += getSessionDurationSeconds(session);
      }
    });

    return buckets.map((bucket) => ({
      ...bucket,
      hours: bucket.seconds / 3600,
    }));
  }, [activeYear, allSessions]);

  const maxMonthlyHours = Math.max(
    ...monthlyStats.map((item) => item.hours),
    1
  );
  const recentTotalPages = Math.max(
    1,
    Math.ceil(recentSessions.length / recentPageSize)
  );
  const recentStartIndex = (recentPage - 1) * recentPageSize;
  const recentPageItems = recentSessions.slice(
    recentStartIndex,
    recentStartIndex + recentPageSize
  );
  const ratingLabel = rating ? ratingLabels[rating] ?? "" : "Not rated";
  const ratingDisabled = isRatingSaving || isRatingLoading || !currentGameId;

  return (
    <main className="relative min-h-screen overflow-hidden bg-slate-950 text-slate-100">
      <Background />
      <div className="relative mx-auto w-full max-w-6xl px-4 py-10 sm:px-6 sm:py-12">
        <AppHeader
          title={gameTitle}
        navItems={navItems}
        path={path}
        onNavigate={onNavigate}
        onLogout={onLogout}
      />

      <section className="mt-10 space-y-6">
        <div className="grid gap-6 lg:grid-cols-[1fr_2fr]">
          <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-5 sm:p-6">
            <div className="aspect-[9/14] overflow-hidden rounded-2xl border border-slate-800 bg-slate-950/60">
              <img
                src={gameImage}
                alt={`${gameTitle} cover`}
                className="h-full w-full object-cover"
              />
            </div>
            <div className="mt-6 rounded-2xl border border-slate-800 bg-gradient-to-br from-amber-400/10 via-slate-950/70 to-emerald-500/10 p-4">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div>
                  <p className="text-xs uppercase tracking-[0.3em] text-amber-300">
                    Your score
                  </p>
                  {isRatingLoading && (
                    <p className="mt-1 text-sm text-slate-200">
                      Loading rating...
                    </p>
                  )}
                </div>
                <button
                  type="button"
                  className="rounded-full border border-slate-800 bg-slate-950/70 px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.25em] text-slate-200 transition hover:border-amber-300/70 hover:text-amber-200 disabled:cursor-not-allowed disabled:border-slate-800 disabled:text-slate-600"
                  onClick={() => handleRateGame(null)}
                  disabled={ratingDisabled || rating === null}
                >
                  {isRatingSaving ? "Clearing..." : "Clear"}
                </button>
              </div>
              <div className="mt-4 flex flex-wrap items-center gap-3">
                <div className="flex items-center gap-2">
                  {[1, 2, 3, 4, 5].map((value) => {
                    const filled = rating !== null && value <= rating;
                    return (
                      <button
                        key={`rating-${value}`}
                        type="button"
                        className={`flex h-11 w-11 items-center justify-center rounded-xl border text-sm font-semibold transition ${
                          filled
                            ? "border-amber-200 bg-amber-400/15 text-amber-100 shadow-[0_10px_40px_-20px_rgba(251,191,36,0.6)]"
                            : "border-slate-800 bg-slate-950/70 text-slate-500 hover:border-amber-200/60 hover:text-amber-200"
                        } ${ratingDisabled ? "cursor-not-allowed opacity-60" : "hover:-translate-y-0.5"}`}
                        disabled={ratingDisabled}
                        onClick={() => handleRateGame(value)}
                      >
                        <RatingStar filled={filled} />
                        <span className="sr-only">Rate {value} out of 5</span>
                      </button>
                    );
                  })}
                </div>
                <span className="text-xs uppercase tracking-[0.2em] text-slate-400">
                  {isRatingLoading
                    ? "Loading rating..."
                    : rating === null
                      ? "Not rated"
                      : `${rating}/5 · ${ratingLabel}`}
                </span>
              </div>
              {ratingError && (
                <p className="mt-3 text-xs text-rose-300">{ratingError}</p>
              )}
              {!ratingError && !currentGameId && sessionsLoaded && (
                <p className="mt-3 text-xs text-slate-400">
                  Add a session for this game to unlock ratings.
                </p>
              )}
            </div>
          </div>
          <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-5 sm:p-6">
            <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
              Recent sessions
              </p>
              <h2 className="mt-2 text-xl font-semibold text-white">
                Latest play snapshots
              </h2>

              <div className="mt-6 space-y-4">
                {gameError ? (
                  <p className="text-xs text-rose-300">{gameError}</p>
                ) : isLoading ? (
                  [1, 2, 3].map((item) => (
                    <div
                      key={`recent-${item}`}
                      className="flex items-center gap-3 rounded-2xl border border-slate-800 bg-slate-950/50 p-4"
                    >
                      <div className="h-14 w-10 rounded-xl bg-slate-800/60" />
                      <div className="flex-1 space-y-2">
                        <div className="h-3 w-32 rounded-full bg-slate-800/60" />
                        <div className="h-2 w-24 rounded-full bg-slate-800/40" />
                      </div>
                      <div className="h-3 w-14 rounded-full bg-slate-800/60" />
                    </div>
                  ))
                ) : recentSessions.length === 0 ? (
                  <div className="rounded-2xl border border-dashed border-slate-800 bg-slate-950/50 p-4 text-sm text-slate-400">
                    No sessions logged for this game yet.
                  </div>
                ) : (
                  recentPageItems.map((session) => (
                    <div
                      key={session.sessionId}
                      className="flex items-center justify-between gap-3 rounded-2xl border border-slate-800 bg-slate-950/50 p-4"
                    >
                      <div className="flex items-center gap-3">
                        <img
                          src={session.capsuleUrl ?? capsuleUrl}
                          alt={`${session.game} capsule art`}
                          className="h-14 w-10 rounded-xl border border-slate-800 object-cover"
                        />
                        <div>
                          <p className="text-sm font-semibold text-white">
                            {formatDateLabel(new Date(session.startedAt))}
                          </p>
                          <p className="text-xs text-slate-400">
                            {formatSessionPlatformLabel(session)} ·{" "}
                            {formatTimeLabel(new Date(session.startedAt))}
                          </p>
                        </div>
                      </div>
                      <p className="text-sm font-semibold text-emerald-200">
                        {formatDuration(
                          getSessionDurationSeconds(session),
                          "short"
                        )}
                      </p>
                    </div>
                  ))
                )}
              </div>
              {recentSessions.length > recentPageSize && (
                <div className="mt-6 flex items-center justify-between text-xs uppercase tracking-[0.2em] text-slate-400">
                  <button
                    type="button"
                    className="rounded-full border border-slate-800 px-4 py-2 text-slate-200 transition hover:border-emerald-400/60 hover:text-emerald-200 disabled:cursor-not-allowed disabled:border-slate-800 disabled:text-slate-600"
                    onClick={() => setRecentPage((page) => Math.max(1, page - 1))}
                    disabled={recentPage === 1}
                  >
                    Prev
                  </button>
                  <span>
                    Page {recentPage} of {recentTotalPages}
                  </span>
                  <button
                    type="button"
                    className="rounded-full border border-slate-800 px-4 py-2 text-slate-200 transition hover:border-emerald-400/60 hover:text-emerald-200 disabled:cursor-not-allowed disabled:border-slate-800 disabled:text-slate-600"
                    onClick={() =>
                      setRecentPage((page) =>
                        Math.min(recentTotalPages, page + 1)
                      )
                    }
                    disabled={recentPage === recentTotalPages}
                  >
                    Next
                  </button>
                </div>
              )}
            </div>
          </div>

        <div className="rounded-3xl border border-slate-800 bg-slate-900/60 px-4 py-4 sm:px-6 sm:py-5">
          <div className="flex flex-wrap items-center justify-between gap-4">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
                Tracking
              </p>
              <p className="mt-2 text-sm text-slate-300">
                {!currentGameId
                  ? "Add a session for this game to enable tracking controls."
                  : isIgnored
                    ? "Scheduler will skip new sessions for this game."
                    : "Scheduler will capture new sessions for this game."}
              </p>
            </div>
            <div className="flex flex-wrap items-center gap-3">
              <span
                className={`rounded-full px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.25em] ${
                  isIgnored
                    ? "border border-rose-400/70 text-rose-100"
                    : "border border-emerald-400/70 text-emerald-100"
                }`}
              >
                {!currentGameId
                  ? "Unknown"
                  : isIgnoreChecking
                    ? "Checking..."
                    : isIgnored
                      ? "Ignored"
                      : "Active"}
              </span>
              <button
                type="button"
                className={`rounded-full px-4 py-2 text-[11px] font-semibold uppercase tracking-[0.2em] transition disabled:cursor-not-allowed disabled:opacity-60 ${
                  isIgnored
                    ? "border border-emerald-300/70 text-emerald-100 hover:border-emerald-200"
                    : "border border-rose-400/70 text-rose-100 hover:border-rose-300"
                }`}
                disabled={
                  isIgnoreChecking || isIgnoreSaving || !currentGameId
                }
                onClick={handleToggleIgnore}
              >
                {isIgnoreSaving
                  ? "Saving..."
                  : isIgnored
                    ? "Resume tracking"
                    : "Ignore game"}
              </button>
            </div>
          </div>
          {ignoreError && (
            <p className="mt-3 text-xs text-rose-300">{ignoreError}</p>
          )}
          {!ignoreError && !currentGameId && sessionsLoaded && (
            <p className="mt-3 text-xs text-slate-400">
              Add a session for this game to enable tracking controls.
            </p>
          )}
        </div>

        <div className="grid grid-cols-1 gap-3 sm:grid-cols-3 sm:gap-6">
          <div className="flex items-center justify-between rounded-xl border border-slate-800 bg-slate-900/70 px-4 py-3 sm:block sm:space-y-2 sm:px-5">
            <p className="text-[11px] uppercase tracking-[0.3em] text-slate-400">
              Hours played
            </p>
            <p className="text-lg font-semibold text-white">
              {formatHours(totalSeconds, 1)}
            </p>
          </div>
          <div className="flex items-center justify-between rounded-xl border border-slate-800 bg-slate-900/70 px-4 py-3 sm:block sm:space-y-2 sm:px-5">
            <p className="text-[11px] uppercase tracking-[0.3em] text-slate-400">
              Sessions
            </p>
            <p className="text-lg font-semibold text-white">
              {allSessions.length}
            </p>
          </div>
          <div className="flex items-center justify-between rounded-xl border border-slate-800 bg-slate-900/70 px-4 py-3 sm:block sm:space-y-2 sm:px-5">
            <p className="text-[11px] uppercase tracking-[0.3em] text-slate-400">
              Last played
            </p>
            <p className="text-lg font-semibold text-white">
              {lastPlayed ? formatDateLabel(lastPlayed) : "—"}
            </p>
          </div>
        </div>

        <div className="space-y-6">
          <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-5 sm:p-6">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
                    All time since {ALL_TIME_START_YEAR}
                  </p>
                  <h2 className="mt-2 text-xl font-semibold text-white">
                    Yearly play time
                  </h2>
                </div>
              </div>

              {gameError ? (
                <p className="mt-4 text-xs text-rose-300">{gameError}</p>
              ) : isLoading ? (
                <div className="mt-6 grid grid-cols-[repeat(auto-fit,minmax(16px,1fr))] gap-2">
                  {Array.from({ length: 12 }, (_, index) => (
                    <div
                      key={`skeleton-${index}`}
                      className="mx-auto h-32 w-2 rounded-md bg-slate-800/60"
                    />
                  ))}
                </div>
              ) : yearlyStats.every((item) => item.seconds === 0) ? (
                <p className="mt-4 text-sm text-slate-400">
                  No sessions recorded for this game since{" "}
                  {ALL_TIME_START_YEAR}.
                </p>
              ) : (
                <div className="mt-6 grid grid-cols-[repeat(auto-fit,minmax(16px,1fr))] items-end gap-2">
                  {yearlyStats.map((year) => {
                    const hoursLabel = `${formatHours(year.seconds, 1)} played`;
                    return (
                      <div key={year.key} className="group relative flex flex-col gap-3">
                        <div className="pointer-events-none absolute left-1/2 top-[-6px] z-10 -translate-x-1/2 -translate-y-full rounded-md bg-slate-900/90 px-3 py-1 text-[10px] font-semibold text-slate-100 opacity-0 shadow-lg ring-1 ring-slate-700/60 transition duration-150 group-hover:opacity-100">
                          {hoursLabel}
                        </div>
                        <div className="flex h-32 items-end justify-center">
                          <div
                            aria-label={`${hoursLabel} in ${year.label}`}
                            title={hoursLabel}
                            className="w-2 rounded-md bg-emerald-400/80"
                            style={{
                              height: `${(year.hours / maxHours) * 100}%`,
                            }}
                          />
                        </div>
                        <div className="text-center text-[10px] uppercase tracking-[0.2em] text-slate-400">
                          {year.label}
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>

            <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-5 sm:p-6">
              <div className="flex flex-wrap items-center justify-between gap-4">
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
                    Year detail
                  </p>
                  <h2 className="mt-2 text-xl font-semibold text-white">
                    Monthly play time
                  </h2>
                </div>
                <div className="flex items-center gap-3 rounded-full border border-slate-800 bg-slate-950/50 px-4 py-2 text-xs uppercase tracking-[0.25em] text-slate-300">
                  <span className="text-slate-500">Year</span>
                  <select
                    className="bg-transparent text-slate-100 outline-none"
                    value={activeYear}
                    onChange={(event) =>
                      setSelectedYear(Number(event.target.value))
                    }
                  >
                    {availableYears
                      .slice()
                      .reverse()
                      .map((year) => (
                        <option key={`year-${year}`} value={year}>
                          {year}
                        </option>
                      ))}
                  </select>
                </div>
              </div>

              {gameError ? (
                <p className="mt-4 text-xs text-rose-300">{gameError}</p>
              ) : isLoading ? (
                <div className="mt-6 grid grid-cols-12 gap-2">
                  {Array.from({ length: 12 }, (_, index) => (
                    <div
                      key={`month-skeleton-${index}`}
                      className="h-32 rounded-full bg-slate-800/60"
                    />
                  ))}
                </div>
              ) : monthlyStats.every((item) => item.seconds === 0) ? (
                <p className="mt-4 text-sm text-slate-400">
                  No sessions recorded in {activeYear}.
                </p>
              ) : (
                <div className="mt-6 grid grid-cols-12 items-end gap-2">
                  {monthlyStats.map((month) => {
                    const hoursLabel = `${formatHours(month.seconds, 1)} played`;
                    return (
                      <div key={month.key} className="group relative flex flex-col gap-3">
                        <div className="pointer-events-none absolute left-1/2 top-[-6px] z-10 -translate-x-1/2 -translate-y-full rounded-md bg-slate-900/90 px-3 py-1 text-[10px] font-semibold text-slate-100 opacity-0 shadow-lg ring-1 ring-slate-700/60 transition duration-150 group-hover:opacity-100">
                          {hoursLabel}
                        </div>
                        <div className="flex h-32 items-end">
                          <div
                            aria-label={`${hoursLabel} in ${month.label}`}
                            title={hoursLabel}
                            className="w-full rounded-full bg-emerald-400/80"
                            style={{
                              height: `${(month.hours / maxMonthlyHours) * 100}%`,
                            }}
                          />
                        </div>
                        <div className="text-center text-[10px] uppercase tracking-[0.2em] text-slate-400">
                          {month.label}
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>
          </div>

        </section>
      </div>
    </main>
  );
};
