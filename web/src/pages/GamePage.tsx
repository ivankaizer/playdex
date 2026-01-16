import { useEffect, useMemo, useState } from "react";

import { apiRequest } from "../api";
import { capsuleUrl, navItems as defaultNavItems } from "../constants";
import type { AccountSession, AccountSessionsResponse, NavItem } from "../types";
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

const formatMonthLabel = (date: Date) =>
  new Intl.DateTimeFormat("en-US", { month: "short" }).format(date);

export const GamePage = ({
  path,
  navItems = defaultNavItems,
  gameTitle,
  onNavigate,
  onLogout,
}: GamePageProps) => {
  const [recentSessions, setRecentSessions] = useState<AccountSession[]>([]);
  const [yearSessions, setYearSessions] = useState<AccountSession[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [gameError, setGameError] = useState("");

  useEffect(() => {
    if (!gameTitle) {
      return;
    }
    let isCancelled = false;

    const loadGameSessions = async () => {
      setIsLoading(true);
      setGameError("");
      try {
        const now = new Date();
        const from = new Date(now);
        from.setUTCFullYear(now.getUTCFullYear() - 1);
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
        firstParams.set("limit", "8");
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

        setRecentSessions(firstResponse.sessions);
        setYearSessions(allSessions);
        setIsLoading(false);
      } catch (err) {
        if (isCancelled) {
          return;
        }
        setRecentSessions([]);
        setYearSessions([]);
        setIsLoading(false);
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

  const gameImage =
    recentSessions.find((session) => session.capsuleUrl)?.capsuleUrl ??
    capsuleUrl;

  const totalSeconds = useMemo(
    () =>
      yearSessions.reduce(
        (sum, session) => sum + getSessionDurationSeconds(session),
        0
      ),
    [yearSessions]
  );

  const lastPlayed = recentSessions[0]
    ? new Date(recentSessions[0].startedAt)
    : null;

  const monthlyStats = useMemo(() => {
    const now = new Date();
    const startOfMonth = new Date(
      Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1)
    );
    const buckets = Array.from({ length: 12 }, (_, index) => {
      const date = new Date(
        Date.UTC(
          startOfMonth.getUTCFullYear(),
          startOfMonth.getUTCMonth() - (11 - index),
          1
        )
      );
      const key = `${date.getUTCFullYear()}-${date.getUTCMonth()}`;
      return {
        key,
        label: formatMonthLabel(date),
        year: date.getUTCFullYear(),
        seconds: 0,
      };
    });

    const bucketMap = new Map(buckets.map((item) => [item.key, item]));
    yearSessions.forEach((session) => {
      const date = new Date(session.startedAt);
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
  }, [yearSessions]);

  const maxHours = Math.max(
    ...monthlyStats.map((item) => item.hours),
    1
  );

  return (
    <main className="relative min-h-screen overflow-hidden bg-slate-950 text-slate-100">
      <Background />
      <div className="relative mx-auto w-full max-w-6xl px-6 py-12">
        <AppHeader
          title="Game"
          navItems={navItems}
          path={path}
          onNavigate={onNavigate}
          onLogout={onLogout}
        />

        <section className="mt-10 space-y-6">
          <div className="grid gap-6 lg:grid-cols-[0.4fr_0.6fr]">
            <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-6">
              <div className="overflow-hidden rounded-2xl border border-slate-800 bg-slate-950/60">
                <img
                  src={gameImage}
                  alt={`${gameTitle} cover`}
                  className="h-80 w-full object-cover"
                />
              </div>
            </div>
            <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-6">
              <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
                Game profile
              </p>
              <h1 className="mt-3 text-3xl font-semibold text-white">
                {gameTitle}
              </h1>
              <p className="mt-2 text-sm text-slate-400">
                Last 12 months of play, tracking sessions, platforms, and play
                time.
              </p>

              <div className="mt-6 grid gap-4 sm:grid-cols-3">
                <div className="rounded-2xl border border-slate-800 bg-slate-950/50 p-4">
                  <p className="text-xs uppercase tracking-[0.3em] text-slate-400">
                    Hours played
                  </p>
                  <p className="mt-2 text-lg font-semibold text-white">
                    {formatHours(totalSeconds, 1)}
                  </p>
                </div>
                <div className="rounded-2xl border border-slate-800 bg-slate-950/50 p-4">
                  <p className="text-xs uppercase tracking-[0.3em] text-slate-400">
                    Sessions
                  </p>
                  <p className="mt-2 text-lg font-semibold text-white">
                    {yearSessions.length}
                  </p>
                </div>
                <div className="rounded-2xl border border-slate-800 bg-slate-950/50 p-4">
                  <p className="text-xs uppercase tracking-[0.3em] text-slate-400">
                    Last played
                  </p>
                  <p className="mt-2 text-lg font-semibold text-white">
                    {lastPlayed ? formatDateLabel(lastPlayed) : "—"}
                  </p>
                </div>
              </div>
            </div>
          </div>

          <div className="grid gap-6 lg:grid-cols-[1.05fr_0.95fr]">
            <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-6">
              <div className="flex items-center justify-between">
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
                    Last 12 months
                  </p>
                  <h2 className="mt-2 text-xl font-semibold text-white">
                    Monthly play time
                  </h2>
                </div>
              </div>

              {gameError ? (
                <p className="mt-4 text-xs text-rose-300">{gameError}</p>
              ) : isLoading ? (
                <div className="mt-6 grid grid-cols-12 gap-2">
                  {Array.from({ length: 12 }, (_, index) => (
                    <div
                      key={`skeleton-${index}`}
                      className="h-32 rounded-full bg-slate-800/60"
                    />
                  ))}
                </div>
              ) : monthlyStats.every((item) => item.seconds === 0) ? (
                <p className="mt-4 text-sm text-slate-400">
                  No sessions recorded for this game in the last year.
                </p>
              ) : (
                <div className="mt-6 grid grid-cols-12 items-end gap-2">
                  {monthlyStats.map((month) => (
                    <div key={month.key} className="flex flex-col gap-3">
                      <div className="flex h-32 items-end">
                        <div
                          title={`${formatHours(month.seconds, 1)}`}
                          className="w-full rounded-full bg-emerald-400/80"
                          style={{
                            height: `${(month.hours / maxHours) * 100}%`,
                          }}
                        />
                      </div>
                      <div className="text-center text-[10px] uppercase tracking-[0.2em] text-slate-400">
                        {month.label}
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>

            <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-6">
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
                  recentSessions.map((session) => (
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
            </div>
          </div>
        </section>
      </div>
    </main>
  );
};
