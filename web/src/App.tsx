import type { FormEvent } from "react";
import { useEffect, useMemo, useState } from "react";

import { apiRequest } from "./api";

type AccountSummary = {
  id: number;
  name: string;
};

type AccountSession = {
  sessionId: number;
  startedAt: string;
  endedAt: string | null;
  platform: string;
  game: string;
  capsuleUrl: string | null;
};

type AccountSessionsResponse = {
  accountId: number;
  sessions: AccountSession[];
  total: number;
  limit: number;
  offset: number;
};

type AccountTodaySessionsResponse = {
  accountId: number;
  sessions: AccountSession[];
};

type AccountCurrentSessionResponse = {
  accountId: number;
  session: AccountSession | null;
};

type AccountStatsPeriod = {
  totalSeconds: number;
  sessionCount: number;
  topGameTitle: string | null;
  platforms: { platform: string; totalSeconds: number }[];
  topGames: { title: string; totalSeconds: number }[];
};

type AccountStatsResponse = {
  accountId: number;
  monthly: AccountStatsPeriod;
  yearly: AccountStatsPeriod;
};

type ProviderStatus = {
  id: number;
  platform: string;
  accountId: string | null;
  status: "active" | "paused";
  apiKey: string | null;
};

type ProvidersResponse = {
  providers: ProviderStatus[];
};

type PlayniteKeyResponse = {
  apiKey: string;
};

const protectedPaths = new Set(["/dashboard", "/sessions", "/settings"]);

export const App = () => {
  const [path, setPath] = useState(() => window.location.pathname);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [status, setStatus] = useState<"idle" | "loading" | "success">("idle");
  const [error, setError] = useState("");
  const [sessionEmail, setSessionEmail] = useState(
    () => localStorage.getItem("playdex.email") ?? ""
  );
  const [isProfileLoading, setIsProfileLoading] = useState(false);
  const [accounts, setAccounts] = useState<AccountSummary[]>([]);
  const [selectedAccountId, setSelectedAccountId] = useState<number | null>(
    null
  );
  const [isAccountsLoading, setIsAccountsLoading] = useState(false);
  const [accountsError, setAccountsError] = useState("");
  const [playniteKey, setPlayniteKey] = useState("");
  const [isPlayniteKeyLoading, setIsPlayniteKeyLoading] = useState(false);
  const [providerStatus, setProviderStatus] = useState<ProviderStatus[]>([]);
  const [isProvidersLoading, setIsProvidersLoading] = useState(false);
  const [providersError, setProvidersError] = useState("");
  const [todaySessions, setTodaySessions] = useState<AccountSession[]>([]);
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
  const [sessionRows, setSessionRows] = useState<AccountSession[]>([]);
  const [sessionsTotal, setSessionsTotal] = useState(0);
  const [sessionsOffset, setSessionsOffset] = useState(0);
  const [isSessionsLoading, setIsSessionsLoading] = useState(false);
  const [sessionsError, setSessionsError] = useState("");
  const sessionsLimit = 10;
  const capsuleUrl =
    "https://shared.steamstatic.com/store_item_assets/steam/apps/2186680/library_600x900_2x.jpg";
  const providerMeta: Record<
    string,
    { label: string; color: string; accent: string }
  > = {
    steam: { label: "S", color: "bg-slate-800", accent: "text-slate-100" },
    psn: { label: "P", color: "bg-indigo-500/30", accent: "text-indigo-200" },
    playnite: {
      label: "PL",
      color: "bg-amber-500/30",
      accent: "text-amber-200",
    },
  };
  const navigate = (target: string) => {
    if (target === path) {
      return;
    }
    window.history.pushState({}, "", target);
    setPath(target);
    window.scrollTo({ top: 0, behavior: "smooth" });
  };

  const navItems = useMemo(
    () => [
      { label: "Dashboard", path: "/dashboard" },
      { label: "Sessions", path: "/sessions" },
      { label: "Settings", path: "/settings" },
    ],
    []
  );
  const handleLogout = () => {
    localStorage.removeItem("playdex.token");
    localStorage.removeItem("playdex.email");
    window.location.assign("/");
  };

  const generatePlayniteKey = async () => {
    setIsPlayniteKeyLoading(true);
    setProvidersError("");
    try {
      const response = await apiRequest<PlayniteKeyResponse>("/playnite/key", {
        method: "POST",
      });
      setPlayniteKey(response.apiKey);
    } catch (err) {
      setProvidersError(
        err instanceof Error ? err.message : "Failed to generate key."
      );
    } finally {
      setIsPlayniteKeyLoading(false);
    }
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError("");
    setStatus("loading");

    try {
      const payload = await apiRequest<{
        userId: number;
        email: string;
        token: string;
      }>("/login", {
        method: "POST",
        body: { email, password },
        auth: false,
      });

      localStorage.setItem("playdex.token", payload.token);
      localStorage.setItem("playdex.email", payload.email);
      setSessionEmail(payload.email);
      setStatus("success");
      window.location.assign("/dashboard");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Login failed.");
      setStatus("idle");
    }
  };

  useEffect(() => {
    const handlePopState = () => setPath(window.location.pathname);
    window.addEventListener("popstate", handlePopState);
    return () => window.removeEventListener("popstate", handlePopState);
  }, []);

  useEffect(() => {
    const token = localStorage.getItem("playdex.token");
    if (!token) {
      setAccounts([]);
      setSelectedAccountId(null);
      return;
    }

    let cancelled = false;
    setIsAccountsLoading(true);
    setAccountsError("");
    const loadAccounts = async () => {
      try {
        const data = await apiRequest<AccountSummary[]>("/accounts");
        if (cancelled) {
          return;
        }
        setAccounts(data);
        setSelectedAccountId((current) => current ?? data[0]?.id ?? null);
        setIsAccountsLoading(false);
      } catch (err) {
        if (!cancelled) {
          setAccounts([]);
          setIsAccountsLoading(false);
          setAccountsError(
            err instanceof Error ? err.message : "Failed to load accounts."
          );
        }
      }
    };

    loadAccounts();
    return () => {
      cancelled = true;
    };
  }, [sessionEmail]);

  useEffect(() => {
    setSessionsOffset(0);
  }, [selectedAccountId]);

  useEffect(() => {
    if (path !== "/dashboard" || !selectedAccountId) {
      return;
    }

    let cancelled = false;
    setIsDashboardLoading(true);
    setDashboardError("");

    const loadDashboard = async () => {
      try {
        const [today, stats, current] = await Promise.all([
          apiRequest<AccountTodaySessionsResponse>(
            `/accounts/${selectedAccountId}/sessions/today`
          ),
          apiRequest<AccountStatsResponse>(
            `/accounts/${selectedAccountId}/stats`
          ),
          apiRequest<AccountCurrentSessionResponse>(
            `/accounts/${selectedAccountId}/sessions/current`
          ),
        ]);

        if (cancelled) {
          return;
        }

        setTodaySessions(today.sessions);
        setMonthlyStats(stats.monthly);
        setYearlyStats(stats.yearly);
        setCurrentSession(current.session);
        setIsDashboardLoading(false);
      } catch (err) {
        if (!cancelled) {
          setTodaySessions([]);
          setMonthlyStats(null);
          setYearlyStats(null);
          setCurrentSession(null);
          setIsDashboardLoading(false);
          setDashboardError(
            err instanceof Error ? err.message : "Failed to load dashboard data."
          );
        }
      }
    };

    loadDashboard();
    return () => {
      cancelled = true;
    };
  }, [path, selectedAccountId]);

  useEffect(() => {
    if (path !== "/sessions" || !selectedAccountId) {
      return;
    }

    let cancelled = false;
    setIsSessionsLoading(true);
    setSessionsError("");

    const loadSessions = async () => {
      try {
        const data = await apiRequest<AccountSessionsResponse>(
          `/accounts/${selectedAccountId}/sessions?limit=${sessionsLimit}&offset=${sessionsOffset}`
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
  }, [path, selectedAccountId, sessionsOffset, sessionsLimit]);

  useEffect(() => {
    if (path !== "/settings") {
      return;
    }

    let cancelled = false;
    setIsProvidersLoading(true);
    setProvidersError("");

    const loadProviders = async () => {
      try {
        const data = await apiRequest<ProvidersResponse>("/providers");
        if (cancelled) {
          return;
        }

        setProviderStatus(data.providers);
        const playnite = data.providers.find(
          (provider) => provider.platform === "playnite"
        );
        if (playnite?.apiKey) {
          setPlayniteKey(playnite.apiKey);
        }
        setIsProvidersLoading(false);
      } catch (err) {
        if (!cancelled) {
          setProviderStatus([]);
          setIsProvidersLoading(false);
          setProvidersError(
            err instanceof Error ? err.message : "Failed to load providers."
          );
        }
      }
    };

    loadProviders();
    return () => {
      cancelled = true;
    };
  }, [path]);

  useEffect(() => {
    const token = localStorage.getItem("playdex.token");
    if (!token) {
      setSessionEmail("");
      setIsProfileLoading(false);
      return;
    }
    if (sessionEmail) {
      return;
    }
    let cancelled = false;
    setIsProfileLoading(true);
    const loadProfile = async () => {
      try {
        const profile = await apiRequest<{ userId: number; email: string }>(
          "/me"
        );

        if (cancelled) {
          return;
        }

        localStorage.setItem("playdex.email", profile.email);
        setSessionEmail(profile.email);
        setIsProfileLoading(false);
      } catch (err) {
        if (!cancelled) {
          setIsProfileLoading(false);
        }
      }
    };

    loadProfile();
    return () => {
      cancelled = true;
    };
  }, [sessionEmail]);

  const background = (
    <div className="pointer-events-none absolute inset-0">
      <div className="absolute -left-24 -top-24 h-72 w-72 rounded-full bg-emerald-400/20 blur-3xl" />
      <div className="absolute right-0 top-1/3 h-96 w-96 rounded-full bg-cyan-400/15 blur-3xl" />
      <div className="absolute bottom-0 left-1/3 h-64 w-64 rounded-full bg-amber-400/10 blur-3xl" />
    </div>
  );
  const renderSparkline = (points: number[], stroke: string) => {
    const width = 64;
    const height = 24;
    const max = Math.max(...points);
    const min = Math.min(...points);
    const range = Math.max(max - min, 1);
    const step = width / (points.length - 1);
    const path = points
      .map((value, index) => {
        const x = index * step;
        const y = height - ((value - min) / range) * height;
        return `${index === 0 ? "M" : "L"}${x} ${y}`;
      })
      .join(" ");

    return (
      <svg
        aria-hidden="true"
        width={width}
        height={height}
        viewBox={`0 0 ${width} ${height}`}
        className="text-emerald-300"
      >
        <path d={path} fill="none" stroke={stroke} strokeWidth="2" />
      </svg>
    );
  };
  const renderPieChart = (
    segments: { label: string; value: number }[],
    colors: string[],
    size = 150
  ) => {
    const strokeWidth = 18;
    const radius = (size - strokeWidth) / 2;
    const circumference = 2 * Math.PI * radius;
    const total = Math.max(
      segments.reduce((sum, segment) => sum + segment.value, 0),
      1
    );
    let offset = 0;

    return (
      <svg
        aria-hidden="true"
        width={size}
        height={size}
        viewBox={`0 0 ${size} ${size}`}
        className="shrink-0"
      >
        <circle
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="transparent"
          stroke="rgba(30,41,59,0.8)"
          strokeWidth={strokeWidth}
        />
        {segments.map((segment, index) => {
          const dash = (segment.value / total) * circumference;
          const gap = circumference - dash;
          const strokeDasharray = `${dash} ${gap}`;
          const strokeDashoffset = -offset;
          offset += dash;
          return (
            <circle
              key={segment.label}
              cx={size / 2}
              cy={size / 2}
              r={radius}
              fill="transparent"
              stroke={colors[index % colors.length]}
              strokeWidth={strokeWidth}
              strokeDasharray={strokeDasharray}
              strokeDashoffset={strokeDashoffset}
              transform={`rotate(-90 ${size / 2} ${size / 2})`}
            />
          );
        })}
      </svg>
    );
  };
  const platformLabels: Record<string, string> = {
    steam: "Steam",
    psn: "PS5",
    playnite: "Playnite",
  };
  const providerNames: Record<string, string> = {
    steam: "Steam",
    psn: "PlayStation",
    playnite: "Playnite",
  };
  const formatPlatformLabel = (platform: string) =>
    platformLabels[platform] ?? platform;
  const formatProviderName = (platform: string) =>
    providerNames[platform] ?? platform;
  const formatDateLabel = (date: Date) =>
    date.toLocaleDateString("en-US", { month: "short", day: "numeric" });
  const formatTimeLabel = (date: Date) =>
    date.toLocaleTimeString("en-US", { hour: "numeric", minute: "2-digit" });
  const formatDuration = (seconds: number, format: "short" | "long") => {
    const safeSeconds = Math.max(0, Math.floor(seconds));
    const hours = Math.floor(safeSeconds / 3600);
    const minutes = Math.floor((safeSeconds % 3600) / 60);
    if (hours <= 0 && minutes <= 0) {
      return format === "short" ? "0m" : "0 min";
    }
    if (format === "short") {
      if (hours > 0) {
        return `${hours}h ${String(minutes).padStart(2, "0")}m`;
      }
      return `${minutes}m`;
    }
    if (hours > 0) {
      return `${hours}h ${minutes}m`;
    }
    return `${minutes} min`;
  };
  const formatHours = (seconds: number, decimals: number) => {
    const hours = seconds / 3600;
    const value =
      decimals === 0
        ? Math.round(hours)
        : Number.isFinite(hours)
          ? Number(hours.toFixed(decimals))
          : 0;
    return `${value}h`;
  };
  const getSessionDurationSeconds = (session: AccountSession) => {
    const started = new Date(session.startedAt).getTime();
    const ended = session.endedAt
      ? new Date(session.endedAt).getTime()
      : Date.now();
    return Math.max(0, Math.floor((ended - started) / 1000));
  };
  const currentSessionStartedAt = currentSession
    ? new Date(currentSession.startedAt)
    : null;
  const currentSessionElapsed = currentSession
    ? formatDuration(getSessionDurationSeconds(currentSession), "long")
    : "0 min";

  const todaySessionCards = useMemo(
    () =>
      todaySessions.map((session) => ({
        title: session.game,
        time: formatDuration(getSessionDurationSeconds(session), "long"),
        platform: formatPlatformLabel(session.platform),
        image: session.capsuleUrl ?? capsuleUrl,
      })),
    [todaySessions]
  );
  const monthlyPlatforms = useMemo(() => {
    if (!monthlyStats) {
      return [];
    }
    const total = Math.max(monthlyStats.totalSeconds, 1);
    return monthlyStats.platforms.map((item) => ({
      label: formatPlatformLabel(item.platform),
      value: Math.round((item.totalSeconds / total) * 100),
      hours: formatHours(item.totalSeconds, 1),
    }));
  }, [monthlyStats]);
  const monthlyTopGames = useMemo(() => {
    if (!monthlyStats) {
      return [];
    }
    return monthlyStats.topGames.map((game) => ({
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
  const yearlyPlatforms = useMemo(() => {
    if (!yearlyStats) {
      return [];
    }
    const total = Math.max(yearlyStats.totalSeconds, 1);
    return yearlyStats.platforms.map((item) => ({
      label: formatPlatformLabel(item.platform),
      value: Math.round((item.totalSeconds / total) * 100),
      hours: formatHours(item.totalSeconds, 0),
    }));
  }, [yearlyStats]);
  const yearlyTopGames = useMemo(() => {
    if (!yearlyStats) {
      return [];
    }
    return yearlyStats.topGames.map((game) => ({
      title: game.title,
      hours: formatHours(game.totalSeconds, 0),
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
      hours: formatHours(yearlyStats.totalSeconds, 0),
      sessions: String(yearlyStats.sessionCount),
      topTitle: yearlyStats.topGameTitle ?? "—",
    };
  }, [yearlyStats]);
  const sessionRowsView = useMemo(
    () =>
      sessionRows.map((session) => ({
        title: session.game,
        platform: formatPlatformLabel(session.platform),
        date: formatDateLabel(new Date(session.startedAt)),
        duration: formatDuration(getSessionDurationSeconds(session), "short"),
        image: session.capsuleUrl ?? capsuleUrl,
      })),
    [sessionRows]
  );
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
  const isLoggedIn = Boolean(localStorage.getItem("playdex.token"));
  const profileLabel =
    sessionEmail || (isProfileLoading ? "Loading..." : "unknown");

  useEffect(() => {
    if (isLoggedIn || !protectedPaths.has(path)) {
      return;
    }
    window.history.replaceState({}, "", "/");
    setPath("/");
  }, [isLoggedIn, path]);

  if (!isLoggedIn && protectedPaths.has(path)) {
    return null;
  }

  if (path === "/dashboard") {
    return (
      <main className="relative min-h-screen overflow-hidden bg-slate-950 text-slate-100">
        {background}
        <div className="relative mx-auto w-full max-w-6xl px-6 py-12">
          <header className="flex flex-wrap items-center justify-between gap-4 rounded-3xl border border-slate-800 bg-slate-900/70 px-6 py-4">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.5em] text-emerald-300">
                Playdex
              </p>
              <h1 className="text-2xl font-semibold text-white">Dashboard</h1>
            </div>
            <nav className="flex flex-wrap items-center gap-3 text-sm">
              {navItems.map((item) => (
                <button
                  key={item.path}
                  type="button"
                  onClick={() => navigate(item.path)}
                  className={`rounded-full px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] transition ${
                    path === item.path
                      ? "bg-emerald-400 text-slate-900"
                      : "border border-slate-700 text-slate-200 hover:border-emerald-400/60"
                  }`}
                >
                  {item.label}
                </button>
              ))}
              <button
                type="button"
                onClick={handleLogout}
                className="rounded-full border border-rose-500/60 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-rose-200 transition hover:border-rose-400 hover:text-rose-100"
              >
                Logout
              </button>
            </nav>
          </header>

          <section className="mt-10 space-y-6">
            {accountsError ? (
              <div className="rounded-2xl border border-rose-500/30 bg-rose-500/10 p-4 text-xs text-rose-200">
                {accountsError}
              </div>
            ) : null}
            <div className="grid gap-6 lg:grid-cols-2">
              <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-6">
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
                      Current session
                    </p>
                    <h2 className="text-2xl font-semibold text-white">
                      {isDashboardLoading
                        ? "Loading..."
                        : currentSession?.game ?? "No active session"}
                    </h2>
                  </div>
                  <span className="rounded-full border border-emerald-400/40 px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.3em] text-emerald-200">
                    {currentSession ? "Live" : "Idle"}
                  </span>
                </div>
                {isDashboardLoading ? (
                  <div className="mt-5 flex flex-wrap gap-5">
                    <div className="h-32 w-24 rounded-2xl border border-slate-800 bg-slate-800/60" />
                    <div className="flex min-w-[220px] flex-1 flex-col gap-4">
                      <div className="flex flex-wrap gap-2">
                        {[1, 2, 3].map((item) => (
                          <div
                            key={item}
                            className="h-6 w-24 rounded-full bg-slate-800/60"
                          />
                        ))}
                      </div>
                      <div className="grid gap-3 sm:grid-cols-2">
                        {[1, 2].map((item) => (
                          <div
                            key={item}
                            className="h-16 rounded-2xl border border-slate-800 bg-slate-950/50"
                          />
                        ))}
                      </div>
                    </div>
                  </div>
                ) : (
                  <div className="mt-5 flex flex-wrap gap-5">
                    {currentSession ? (
                      <img
                        src={currentSession.capsuleUrl ?? capsuleUrl}
                        alt={`${currentSession.game} capsule art`}
                        className="h-32 w-24 rounded-2xl border border-slate-800 object-cover shadow-[0_15px_40px_rgba(15,23,42,0.55)]"
                      />
                    ) : (
                      <div className="h-32 w-24 rounded-2xl border border-dashed border-slate-800 bg-slate-950/40" />
                    )}
                    <div className="flex min-w-[220px] flex-1 flex-col gap-4">
                      <div className="flex flex-wrap gap-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-300">
                        {currentSession ? (
                          <>
                            <span className="rounded-full border border-slate-700 px-3 py-1">
                              {formatPlatformLabel(currentSession.platform)}
                            </span>
                            <span className="rounded-full border border-slate-700 px-3 py-1">
                              Started{" "}
                              {currentSessionStartedAt
                                ? formatTimeLabel(currentSessionStartedAt)
                                : "--"}
                            </span>
                            <span className="rounded-full border border-slate-700 px-3 py-1">
                              Elapsed {currentSessionElapsed}
                            </span>
                          </>
                        ) : (
                          <span className="rounded-full border border-slate-700 px-3 py-1">
                            Not tracking
                          </span>
                        )}
                      </div>
                      <div className="grid gap-3 sm:grid-cols-2">
                        {[
                          { label: "Last sync", value: "--" },
                          { label: "Streak", value: "--" },
                        ].map((stat) => (
                          <div
                            key={stat.label}
                            className="rounded-2xl border border-slate-800 bg-slate-950/50 px-4 py-3"
                          >
                            <p className="text-[10px] uppercase tracking-[0.3em] text-slate-400">
                              {stat.label}
                            </p>
                            <p className="mt-2 text-sm font-semibold text-white">
                              {stat.value}
                            </p>
                          </div>
                        ))}
                      </div>
                      <div className="flex flex-wrap gap-2 text-xs text-slate-300">
                        <button
                          type="button"
                          disabled={!currentSession}
                          className={`rounded-full border border-slate-700 px-4 py-2 font-semibold uppercase tracking-[0.3em] text-slate-200 transition ${
                            currentSession
                              ? "hover:border-emerald-400/60"
                              : "cursor-not-allowed opacity-50"
                          }`}
                        >
                          Stop tracking
                        </button>
                      </div>
                    </div>
                  </div>
                )}
              </div>

              <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-6">
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
                      Today sessions
                    </p>
                    <h2 className="text-xl font-semibold text-white">
                      {todaySessionCards.length}{" "}
                      {todaySessionCards.length === 1 ? "session" : "sessions"}{" "}
                      logged
                    </h2>
                  </div>
                  <button
                    type="button"
                    onClick={() => navigate("/sessions")}
                    className="rounded-full border border-slate-700 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-200 transition hover:border-emerald-400/60"
                  >
                    View all
                  </button>
                </div>
                <div className="mt-5 space-y-4">
                  {dashboardError ? (
                    <div className="rounded-2xl border border-rose-500/30 bg-rose-500/10 p-4 text-xs text-rose-200">
                      {dashboardError}
                    </div>
                  ) : isDashboardLoading ? (
                    [1, 2, 3].map((item) => (
                      <div
                        key={item}
                        className="flex items-center justify-between gap-4 rounded-2xl border border-slate-800 bg-slate-950/50 p-4"
                      >
                        <div className="flex items-center gap-3">
                          <div className="h-14 w-10 rounded-xl bg-slate-800/60" />
                          <div className="space-y-2">
                            <div className="h-3 w-28 rounded-full bg-slate-800/70" />
                            <div className="h-2 w-16 rounded-full bg-slate-800/50" />
                          </div>
                        </div>
                        <div className="h-3 w-12 rounded-full bg-slate-800/60" />
                      </div>
                    ))
                  ) : todaySessionCards.length === 0 ? (
                    <div className="rounded-2xl border border-dashed border-slate-800 bg-slate-950/40 p-4 text-xs text-slate-400">
                      No sessions logged today yet.
                    </div>
                  ) : (
                    todaySessionCards.map((session) => (
                      <div
                        key={session.title}
                        className="relative flex flex-wrap items-center justify-between gap-4 overflow-hidden rounded-2xl border border-slate-800 bg-slate-950/50 p-4"
                        style={{
                          backgroundImage: `url(${session.image})`,
                          backgroundSize: "cover",
                          backgroundPosition: "center",
                        }}
                      >
                        <div className="absolute inset-0 bg-slate-950/80" />
                        <div className="relative z-10">
                          <p className="text-sm font-semibold text-white">
                            {session.title}
                          </p>
                        </div>
                        <div className="relative z-10 flex items-center gap-4">
                          <p className="text-xs text-slate-400">
                            {session.platform}
                          </p>
                          <p className="text-sm font-semibold text-emerald-200">
                            {session.time}
                          </p>
                        </div>
                      </div>
                    ))
                  )}
                </div>
              </div>
            </div>

            <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-6">
              <div className="flex flex-wrap items-baseline gap-3">
                <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
                  Monthly stats
                </p>
                <p className="text-sm font-semibold uppercase tracking-[0.2em] text-slate-200">
                  April highlights
                </p>
              </div>
              <div className="mt-5 grid gap-4 sm:grid-cols-3">
                {[
                  { label: "Hours played", value: monthlySummary.hours },
                  { label: "Sessions", value: monthlySummary.sessions },
                  { label: "Top title", value: monthlySummary.topTitle },
                ].map((stat) => (
                  <div
                    key={stat.label}
                    className="rounded-2xl border border-slate-800 bg-slate-950/50 p-4"
                  >
                    <p className="text-xs uppercase tracking-[0.3em] text-slate-400">
                      {stat.label}
                    </p>
                    <p className="mt-2 text-lg font-semibold text-white">
                      {stat.value}
                    </p>
                  </div>
                ))}
              </div>
              <div className="mt-5 grid gap-4 lg:grid-cols-[0.9fr_1.1fr]">
                <div className="rounded-2xl border border-slate-800 bg-slate-950/50 p-4">
                  <p className="text-xs uppercase tracking-[0.3em] text-slate-400">
                    Platform distribution
                  </p>
                  <div className="mt-4 flex flex-wrap items-center gap-4">
                    {renderPieChart(
                      monthlyPlatforms,
                      ["#34d399", "#22d3ee", "#a78bfa", "#f59e0b"],
                      168
                    )}
                    <div className="flex-1 space-y-2 text-xs text-slate-300">
                      {monthlyPlatforms.map((item, index) => (
                        <div
                          key={item.label}
                          className="flex items-center justify-between gap-3"
                        >
                          <div className="flex items-center gap-2">
                            <span
                              className="h-2 w-2 rounded-full"
                              style={{
                                backgroundColor: [
                                  "#34d399",
                                  "#22d3ee",
                                  "#a78bfa",
                                  "#f59e0b",
                                ][index % 4],
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
                </div>
                <div className="rounded-2xl border border-slate-800 bg-slate-950/50 p-4">
                  <p className="text-xs uppercase tracking-[0.3em] text-slate-400">
                    Top 5 games
                  </p>
                  <div className="mt-4 space-y-3">
                    {monthlyTopGames.map((game, index) => (
                      <div
                        key={game.title}
                        className="flex items-center justify-between text-sm"
                      >
                        <div className="flex items-center gap-3">
                          <span className="text-xs font-semibold text-slate-400">
                            {String(index + 1).padStart(2, "0")}
                          </span>
                          <span className="font-semibold text-white">
                            {game.title}
                          </span>
                        </div>
                        <span className="text-xs font-semibold text-emerald-200">
                          {game.hours}
                        </span>
                      </div>
                    ))}
                  </div>
                </div>
              </div>
            </div>

            <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-6">
              <div className="flex flex-wrap items-baseline gap-3">
                <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
                  Yearly stats
                </p>
                <p className="text-sm font-semibold uppercase tracking-[0.2em] text-slate-200">
                  2025 recap
                </p>
              </div>
              <div className="mt-5 grid gap-4 sm:grid-cols-3">
                {[
                  { label: "Hours played", value: yearlySummary.hours },
                  { label: "Sessions", value: yearlySummary.sessions },
                  { label: "Top title", value: yearlySummary.topTitle },
                ].map((stat) => (
                  <div
                    key={stat.label}
                    className="rounded-2xl border border-slate-800 bg-slate-950/50 p-4"
                  >
                    <p className="text-xs uppercase tracking-[0.3em] text-slate-400">
                      {stat.label}
                    </p>
                    <p className="mt-2 text-lg font-semibold text-white">
                      {stat.value}
                    </p>
                  </div>
                ))}
              </div>
              <div className="mt-5 grid gap-4 lg:grid-cols-[0.9fr_1.1fr]">
                <div className="rounded-2xl border border-slate-800 bg-slate-950/50 p-4">
                  <p className="text-xs uppercase tracking-[0.3em] text-slate-400">
                    Platform distribution
                  </p>
                  <div className="mt-4 flex flex-wrap items-center gap-4">
                    {renderPieChart(
                      yearlyPlatforms,
                      ["#34d399", "#22d3ee", "#a78bfa", "#f59e0b"],
                      168
                    )}
                    <div className="flex-1 space-y-2 text-xs text-slate-300">
                      {yearlyPlatforms.map((item, index) => (
                        <div
                          key={item.label}
                          className="flex items-center justify-between gap-3"
                        >
                          <div className="flex items-center gap-2">
                            <span
                              className="h-2 w-2 rounded-full"
                              style={{
                                backgroundColor: [
                                  "#34d399",
                                  "#22d3ee",
                                  "#a78bfa",
                                  "#f59e0b",
                                ][index % 4],
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
                </div>
                <div className="rounded-2xl border border-slate-800 bg-slate-950/50 p-4">
                  <p className="text-xs uppercase tracking-[0.3em] text-slate-400">
                    Top 5 games
                  </p>
                  <div className="mt-4 space-y-3">
                    {yearlyTopGames.map((game, index) => (
                      <div
                        key={game.title}
                        className="flex items-center justify-between text-sm"
                      >
                        <div className="flex items-center gap-3">
                          <span className="text-xs font-semibold text-slate-400">
                            {String(index + 1).padStart(2, "0")}
                          </span>
                          <span className="font-semibold text-white">
                            {game.title}
                          </span>
                        </div>
                        <span className="text-xs font-semibold text-emerald-200">
                          {game.hours}
                        </span>
                      </div>
                    ))}
                  </div>
                </div>
              </div>
            </div>
          </section>
        </div>
      </main>
    );
  }

  if (path === "/settings") {
    return (
      <main className="relative min-h-screen overflow-hidden bg-slate-950 text-slate-100">
        {background}
        <div className="relative mx-auto w-full max-w-6xl px-6 py-12">
          <header className="flex flex-wrap items-center justify-between gap-4 rounded-3xl border border-slate-800 bg-slate-900/70 px-6 py-4">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.5em] text-emerald-300">
                Playdex
              </p>
              <h1 className="text-2xl font-semibold text-white">Settings</h1>
            </div>
            <nav className="flex flex-wrap items-center gap-3 text-sm">
              {navItems.map((item) => (
                <button
                  key={item.path}
                  type="button"
                  onClick={() => navigate(item.path)}
                  className={`rounded-full px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] transition ${
                    path === item.path
                      ? "bg-emerald-400 text-slate-900"
                      : "border border-slate-700 text-slate-200 hover:border-emerald-400/60"
                  }`}
                >
                  {item.label}
                </button>
              ))}
              <button
                type="button"
                onClick={handleLogout}
                className="rounded-full border border-rose-500/60 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-rose-200 transition hover:border-rose-400 hover:text-rose-100"
              >
                Logout
              </button>
            </nav>
          </header>

          <section className="mt-10 grid gap-6 lg:grid-cols-[1.05fr_0.95fr]">
            <div className="space-y-6">
              <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-6">
                <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
                  Add provider
                </p>
                <h2 className="mt-2 text-xl font-semibold text-white">
                  Connect new accounts
                </h2>
                <div className="mt-6 grid gap-4">
                  <div className="rounded-2xl border border-slate-800 bg-slate-950/50 p-4">
                    <p className="text-sm font-semibold text-white">Steam</p>
                    <div className="mt-4 grid gap-3 sm:grid-cols-2">
                      <label className="text-xs font-medium text-slate-300">
                        API Key
                        <input
                          type="text"
                          placeholder="STEAM_API_KEY"
                          className="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950/80 px-3 py-2 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                        />
                      </label>
                      <label className="text-xs font-medium text-slate-300">
                        Account ID
                        <input
                          type="text"
                          placeholder="7656119..."
                          className="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950/80 px-3 py-2 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                        />
                      </label>
                    </div>
                    <button
                      type="button"
                      className="mt-4 rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-300"
                    >
                      Add Steam
                    </button>
                  </div>

                  <div className="rounded-2xl border border-slate-800 bg-slate-950/50 p-4">
                    <p className="text-sm font-semibold text-white">PSN</p>
                    <label className="mt-4 block text-xs font-medium text-slate-300">
                      NPSSO Code
                      <input
                        type="text"
                        placeholder="NPSSO..."
                        className="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950/80 px-3 py-2 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                      />
                    </label>
                    <button
                      type="button"
                      className="mt-4 rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-300"
                    >
                      Add PSN
                    </button>
                  </div>

                  <div className="rounded-2xl border border-slate-800 bg-slate-950/50 p-4">
                    <p className="text-sm font-semibold text-white">Playnite</p>
                    <p className="mt-2 text-xs text-slate-400">
                      Generate an API key for the local Playnite client.
                    </p>
                    <div className="mt-4 flex flex-wrap items-center gap-3">
                      <button
                        type="button"
                        onClick={generatePlayniteKey}
                        disabled={isPlayniteKeyLoading}
                        className="rounded-full border border-emerald-400/60 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-emerald-200 transition hover:border-emerald-300"
                      >
                        {isPlayniteKeyLoading ? "Generating..." : "Generate key"}
                      </button>
                      <span className="rounded-full border border-slate-700 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-200">
                        {playniteKey || "Not generated yet"}
                      </span>
                    </div>
                    <button
                      type="button"
                      className="mt-4 rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-300"
                    >
                      Save Playnite
                    </button>
                  </div>
                </div>
              </div>
            </div>

            <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-6">
              <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
                Manage providers
              </p>
              <h2 className="mt-2 text-xl font-semibold text-white">
                Active connections
              </h2>
              {providersError ? (
                <p className="mt-4 text-xs text-rose-300">{providersError}</p>
              ) : null}
              <div className="mt-6 space-y-4">
                {isProvidersLoading ? (
                  [1, 2].map((item) => (
                    <div
                      key={item}
                      className="flex items-center justify-between gap-4 rounded-2xl border border-slate-800 bg-slate-950/50 p-4"
                    >
                      <div className="flex items-center gap-3">
                        <div className="h-10 w-10 rounded-full bg-slate-800/60" />
                        <div className="space-y-2">
                          <div className="h-3 w-24 rounded-full bg-slate-800/60" />
                          <div className="h-2 w-16 rounded-full bg-slate-800/50" />
                        </div>
                      </div>
                      <div className="h-6 w-20 rounded-full bg-slate-800/60" />
                    </div>
                  ))
                ) : providerStatus.length === 0 ? (
                  <div className="rounded-2xl border border-dashed border-slate-800 bg-slate-950/40 p-4 text-xs text-slate-400">
                    No providers connected yet. Add one to start syncing
                    playtime.
                  </div>
                ) : (
                  providerStatus.map((provider) => (
                    <div
                      key={provider.id}
                      className="flex flex-wrap items-center justify-between gap-4 rounded-2xl border border-slate-800 bg-slate-950/50 p-4"
                    >
                      <div className="flex items-center gap-3">
                        <div
                          className={`flex h-10 w-10 items-center justify-center rounded-full ${
                            providerMeta[provider.platform]?.color ??
                            "bg-slate-800"
                          }`}
                        >
                          <span
                            className={`text-xs font-semibold uppercase ${
                              providerMeta[provider.platform]?.accent ??
                              "text-slate-200"
                            }`}
                          >
                            {providerMeta[provider.platform]?.label ?? "PX"}
                          </span>
                        </div>
                        <div>
                          <div className="flex items-center gap-2">
                            <p className="text-sm font-semibold text-white">
                              {formatProviderName(provider.platform)}
                            </p>
                          </div>
                          <p className="text-xs text-slate-400">
                            {provider.accountId ?? "Local"}
                          </p>
                        </div>
                      </div>
                      <div className="flex flex-wrap items-center gap-2 text-xs">
                        <button
                          type="button"
                          onClick={() =>
                            setProviderStatus((current) =>
                              current.map((item) =>
                                item.id === provider.id
                                  ? {
                                      ...item,
                                      status:
                                        item.status === "active"
                                          ? "paused"
                                          : "active",
                                    }
                                  : item
                              )
                            )
                          }
                          className={`rounded-full px-3 py-2 font-semibold uppercase tracking-[0.3em] ${
                            provider.status === "active"
                              ? "bg-emerald-400 text-slate-900"
                              : "border border-slate-700 text-slate-200"
                          }`}
                        >
                          {provider.status === "active" ? "Active" : "Paused"}
                        </button>
                        <button
                          type="button"
                          className="rounded-full border border-slate-700 px-3 py-2 font-semibold uppercase tracking-[0.3em] text-slate-200"
                        >
                          Edit
                        </button>
                        <button
                          type="button"
                          className="rounded-full border border-rose-500/50 px-3 py-2 font-semibold uppercase tracking-[0.3em] text-rose-300"
                        >
                          Remove
                        </button>
                      </div>
                    </div>
                  ))
                )}
              </div>
            </div>
          </section>
        </div>
      </main>
    );
  }

  if (path === "/sessions") {
    return (
      <main className="relative min-h-screen overflow-hidden bg-slate-950 text-slate-100">
        {background}
        <div className="relative mx-auto w-full max-w-6xl px-6 py-12">
          <header className="flex flex-wrap items-center justify-between gap-4 rounded-3xl border border-slate-800 bg-slate-900/70 px-6 py-4">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.5em] text-emerald-300">
                Playdex
              </p>
              <h1 className="text-2xl font-semibold text-white">Sessions</h1>
            </div>
            <nav className="flex flex-wrap items-center gap-3 text-sm">
              {navItems.map((item) => (
                <button
                  key={item.path}
                  type="button"
                  onClick={() => navigate(item.path)}
                  className={`rounded-full px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] transition ${
                    path === item.path
                      ? "bg-emerald-400 text-slate-900"
                      : "border border-slate-700 text-slate-200 hover:border-emerald-400/60"
                  }`}
                >
                  {item.label}
                </button>
              ))}
              <button
                type="button"
                onClick={handleLogout}
                className="rounded-full border border-rose-500/60 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-rose-200 transition hover:border-rose-400 hover:text-rose-100"
              >
                Logout
              </button>
            </nav>
          </header>

          <section className="mt-10 rounded-3xl border border-slate-800 bg-slate-900/60 p-6">
            {accountsError ? (
              <div className="mb-4 rounded-2xl border border-rose-500/30 bg-rose-500/10 p-4 text-xs text-rose-200">
                {accountsError}
              </div>
            ) : null}
            <div className="flex flex-wrap items-center justify-between gap-4">
              <div>
                <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
                  Sessions log
                </p>
                <h2 className="mt-2 text-xl font-semibold text-white">
                  Every session tracked
                </h2>
              </div>
            </div>

            <div className="mt-6 grid gap-3 md:grid-cols-[1.2fr_0.6fr_0.6fr]">
              <label className="text-xs font-medium text-slate-300">
                Title
                <input
                  type="text"
                  placeholder="Search by game title"
                  className="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950/80 px-3 py-2 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                />
              </label>
              <label className="text-xs font-medium text-slate-300">
                From
                <input
                  type="date"
                  className="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950/80 px-3 py-2 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                />
              </label>
              <label className="text-xs font-medium text-slate-300">
                To
                <input
                  type="date"
                  className="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950/80 px-3 py-2 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                />
              </label>
            </div>
            {sessionsError ? (
              <p className="mt-4 text-xs text-rose-300">{sessionsError}</p>
            ) : null}

            <div className="mt-6 overflow-hidden rounded-2xl border border-slate-800">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-950/70 text-xs uppercase tracking-[0.3em] text-slate-400">
                  <tr>
                    {[
                      "Title",
                      "Platform",
                      "Date",
                      "Duration",
                    ].map((label, index) => (
                      <th
                        key={label}
                        className={`px-4 py-3 ${
                          index === 3 ? "text-right" : "text-left"
                        }`}
                      >
                        <button
                          type="button"
                          className="inline-flex items-center gap-2 text-xs uppercase tracking-[0.3em] text-slate-400 transition hover:text-slate-200"
                        >
                          {label}
                          <svg
                            aria-hidden="true"
                            viewBox="0 0 20 20"
                            className="h-3 w-3"
                            fill="currentColor"
                          >
                            <path d="M6 8l4-4 4 4H6zM14 12l-4 4-4-4h8z" />
                          </svg>
                        </button>
                      </th>
                    ))}
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
                      </tr>
                    ))
                  ) : sessionRowsView.length === 0 ? (
                    <tr>
                      <td
                        colSpan={4}
                        className="px-4 py-10 text-center text-sm text-slate-400"
                      >
                        No sessions yet. Sync a provider to populate this list.
                      </td>
                    </tr>
                  ) : (
                    sessionRowsView.map((session) => (
                      <tr
                        key={`${session.title}-${session.date}`}
                        className="transition hover:bg-slate-900/70"
                      >
                        <td className="px-4 py-3">
                          <div className="flex items-center gap-3">
                            <img
                              src={session.image}
                              alt={`${session.title} capsule art`}
                              className="h-12 w-9 rounded-lg border border-slate-800 object-cover"
                            />
                            <span className="font-semibold text-white">
                              {session.title}
                            </span>
                          </div>
                        </td>
                        <td className="px-4 py-3 text-slate-300">
                          {session.platform}
                        </td>
                        <td className="px-4 py-3 text-slate-300">
                          {session.date}
                        </td>
                        <td className="px-4 py-3 text-right text-emerald-200">
                          {session.duration}
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>

            <div className="mt-6 flex flex-wrap items-center justify-between gap-4 text-xs text-slate-300">
              <p>
                Showing {sessionsStart}-{sessionsEnd} of {sessionsTotal}{" "}
                sessions
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
                    onClick={() =>
                      setSessionsOffset((page - 1) * sessionsLimit)
                    }
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
      </main>
    );
  }

  return (
    <main className="relative min-h-screen overflow-hidden bg-slate-950 text-slate-100">
      {background}

      <div className="relative mx-auto w-full max-w-6xl px-6 py-16">
        <div className="grid gap-12 lg:grid-cols-[1.1fr_0.9fr]">
          <section className="flex flex-col justify-center gap-8">
            <div className="flex items-center gap-3">
              <span className="inline-flex h-3 w-3 rounded-full bg-emerald-400 shadow-[0_0_14px_rgba(52,211,153,0.8)]" />
              <p className="text-xs font-semibold uppercase tracking-[0.5em] text-emerald-300">
                Playdex
              </p>
              <span className="rounded-full border border-emerald-400/40 px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.3em] text-emerald-200">
                Beta
              </span>
            </div>

            <div className="space-y-4">
              <h1 className="text-balance text-4xl font-semibold leading-tight text-white sm:text-5xl">
                Your cross-platform playtime, perfectly organized.
              </h1>
              <p className="max-w-xl text-lg text-slate-300">
                Connect your gaming accounts and keep a living history of your
                sessions, streaks, and favorite titles across platforms.
              </p>
            </div>
          </section>

          <section className="flex items-center justify-center">
            <div className="w-full max-w-md rounded-3xl border border-slate-800 bg-slate-900/70 p-8 shadow-[0_25px_80px_rgba(15,23,42,0.65)]">
              {isLoggedIn ? (
                <div className="space-y-5 text-center">
                  <p className="text-xs font-semibold uppercase tracking-[0.4em] text-emerald-300">
                    Signed in
                  </p>
                  <div className="space-y-2">
                    <h2 className="text-2xl font-semibold text-white">
                      Welcome back
                    </h2>
                    <p className="text-sm text-slate-300">
                      Logged in as{" "}
                      <span className="font-semibold text-emerald-200">
                        {profileLabel}
                      </span>
                    </p>
                  </div>
                  <button
                    type="button"
                    onClick={handleLogout}
                    className="w-full rounded-xl border border-rose-500/60 px-6 py-3 text-sm font-semibold uppercase tracking-[0.3em] text-rose-200 transition hover:border-rose-400 hover:text-rose-100"
                  >
                    Logout
                  </button>
                </div>
              ) : (
                <form className="space-y-5" onSubmit={handleSubmit}>
                  <label className="block text-sm font-medium text-slate-200">
                    Email address
                    <input
                      type="email"
                      placeholder="you@example.com"
                      value={email}
                      onChange={(event) => setEmail(event.target.value)}
                      className="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950/80 px-4 py-3 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                    />
                  </label>
                  <label className="block text-sm font-medium text-slate-200">
                    Password
                    <input
                      type="password"
                      placeholder="••••••••"
                      value={password}
                      onChange={(event) => setPassword(event.target.value)}
                      className="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950/80 px-4 py-3 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                    />
                  </label>

                  <button
                    type="submit"
                    disabled={status === "loading"}
                    className="w-full rounded-xl bg-emerald-400 px-6 py-3 text-sm font-semibold text-slate-900 transition hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-emerald-300/60"
                  >
                    {status === "loading" ? "Signing in..." : "Log in"}
                  </button>
                </form>
              )}

              <div className="mt-6 text-center text-xs text-slate-400">
                {isLoggedIn ? (
                  <p className="text-emerald-300">Session active.</p>
                ) : error ? (
                  <p className="text-rose-300">{error}</p>
                ) : status === "success" ? (
                  <p className="text-emerald-300">Signed in successfully.</p>
                ) : (
                  <p>Need access? Ask an admin to invite you.</p>
                )}
              </div>
            </div>
          </section>
        </div>

        <section className="mt-12">
          <div>
            <h2 className="text-2xl font-semibold text-white">
              Everything in one place
            </h2>
            <p className="mt-3 text-sm text-slate-300">
              Keep your play history consistent, discover trends, and set goals
              with a dashboard built for long-term tracking.
            </p>
          </div>
          <div className="mt-6 grid gap-3 sm:grid-cols-3">
            {[
              {
                label: "Track Steam and PSN playtime",
                icon: (
                  <svg
                    aria-hidden="true"
                    viewBox="0 0 24 24"
                    className="h-5 w-5"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="1.5"
                  >
                    <circle cx="12" cy="12" r="8" />
                    <path strokeLinecap="round" d="M12 8v4l3 2" />
                  </svg>
                ),
              },
              {
                label: "Automatic polling with scheduled refresh",
                icon: (
                  <svg
                    aria-hidden="true"
                    viewBox="0 0 24 24"
                    className="h-5 w-5"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="1.5"
                  >
                    <path
                      strokeLinecap="round"
                      d="M20 8v-3m0 0h-3m3 0-3.5 3.5"
                    />
                    <path
                      strokeLinecap="round"
                      d="M4 16v3m0 0h3m-3 0 3.5-3.5"
                    />
                    <path
                      strokeLinecap="round"
                      d="M6.5 6.5a8 8 0 0 1 11 0M6.5 17.5a8 8 0 0 0 11 0"
                    />
                  </svg>
                ),
              },
              {
                label: "Fast search across your library",
                icon: (
                  <svg
                    aria-hidden="true"
                    viewBox="0 0 24 24"
                    className="h-5 w-5"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="1.5"
                  >
                    <circle cx="11" cy="11" r="6.5" />
                    <path strokeLinecap="round" d="m16 16 4 4" />
                  </svg>
                ),
              },
            ].map((feature) => (
              <div
                key={feature.label}
                className="flex items-center gap-3 rounded-2xl border border-slate-800 bg-slate-950/60 p-4 text-sm text-slate-200"
              >
                <span className="flex h-9 w-9 items-center justify-center rounded-full bg-emerald-400/10 text-emerald-300">
                  {feature.icon}
                </span>
                <span>{feature.label}</span>
              </div>
            ))}
          </div>
        </section>
      </div>
    </main>
  );
};
