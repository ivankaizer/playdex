import type { FormEvent } from "react";
import { useEffect, useState } from "react";

import { apiRequest } from "../api";
import { Background } from "../components/Background";
import { StatsRecapSection } from "../components/StatsRecapSection";
import { ThemeToggle } from "../components/ThemeToggle";

type HomePageProps = {
  isLoggedIn: boolean;
  onDashboardRedirect: () => void;
  onLogout: () => void;
  theme: "light" | "dark";
  onToggleTheme: () => void;
};

const previewPlatformColors = ["#34d399", "#38bdf8", "#f59e0b", "#a78bfa"];

const previewRecapSummary = [
  { label: "Hours played", value: "124.8h" },
  { label: "Sessions", value: "93" },
  { label: "Top title", value: "Hades II" },
];

const previewRecapPlatforms = [
  { label: "Steam", value: 52, hours: "64.9h" },
  { label: "PlayStation", value: 30, hours: "37.4h" },
  { label: "Xbox", value: 12, hours: "14.9h" },
  { label: "Switch", value: 6, hours: "7.6h" },
];

const previewRecapTopGames = [
  { gameId: "hades-2", title: "Hades II", hours: "26.4h" },
  { gameId: "bg3", title: "Baldur's Gate 3", hours: "18.2h" },
  { gameId: "re4", title: "Resident Evil 4", hours: "14.7h" },
  { gameId: "helldivers-2", title: "Helldivers 2", hours: "12.6h" },
  { gameId: "fh5", title: "Forza Horizon 5", hours: "10.8h" },
];

const previewRecapCards = [
  {
    gameId: "hades-2",
    title: "Hades II",
    hours: "26.4h",
    artClass:
      "bg-gradient-to-br from-rose-400/60 via-fuchsia-500/40 to-slate-950/70",
  },
  {
    gameId: "bg3",
    title: "Baldur's Gate 3",
    hours: "18.2h",
    artClass:
      "bg-gradient-to-br from-amber-300/60 via-orange-500/40 to-slate-950/70",
  },
  {
    gameId: "re4",
    title: "Resident Evil 4",
    hours: "14.7h",
    artClass:
      "bg-gradient-to-br from-emerald-300/60 via-teal-500/40 to-slate-950/70",
  },
  {
    gameId: "helldivers-2",
    title: "Helldivers 2",
    hours: "12.6h",
    artClass:
      "bg-gradient-to-br from-sky-300/60 via-cyan-500/40 to-slate-950/70",
  },
  {
    gameId: "fh5",
    title: "Forza Horizon 5",
    hours: "10.8h",
    artClass:
      "bg-gradient-to-br from-violet-300/60 via-indigo-500/40 to-slate-950/70",
  },
];

export const HomePage = ({
  isLoggedIn,
  onDashboardRedirect,
  onLogout,
  theme,
  onToggleTheme,
}: HomePageProps) => {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [status, setStatus] = useState<"idle" | "loading" | "success">("idle");
  const [error, setError] = useState("");
  const [sessionEmail, setSessionEmail] = useState(
    () => localStorage.getItem("playdex.email") ?? ""
  );
  const [isProfileLoading, setIsProfileLoading] = useState(false);

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError("");
    setStatus("loading");

    try {
      const payload = await apiRequest<{
        userId: number;
        email: string;
        accessToken: string;
        refreshToken: string;
      }>("/login", {
        method: "POST",
        body: { email, password },
        auth: false,
      });

      localStorage.setItem("playdex.token", payload.accessToken);
      localStorage.setItem("playdex.refresh", payload.refreshToken);
      localStorage.setItem("playdex.email", payload.email);
      setSessionEmail(payload.email);
      setStatus("success");
      onDashboardRedirect();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Login failed.");
      setStatus("idle");
    }
  };

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
      } catch {
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

  const profileLabel =
    sessionEmail || (isProfileLoading ? "Loading..." : "unknown");

  return (
    <main className="relative min-h-screen overflow-hidden bg-slate-200 dark:bg-slate-950 text-slate-900 dark:text-slate-100">
      <Background />

      <div className="relative mx-auto w-full max-w-6xl px-4 py-12 sm:px-6 sm:py-16">
        <div className="flex justify-end mb-6">
          <ThemeToggle theme={theme} onToggle={onToggleTheme} />
        </div>
        <div className="grid gap-12 lg:grid-cols-[1.1fr_0.9fr]">
          <section className="flex flex-col justify-center gap-8">
            <div className="flex items-center gap-3">
              <span className="inline-flex h-3 w-3 rounded-full bg-emerald-400 shadow-[0_0_14px_rgba(52,211,153,0.8)]" />
              <p className="text-xs font-semibold uppercase tracking-[0.5em] text-emerald-700 dark:text-emerald-300">
                Playdex
              </p>
            </div>

            <div className="space-y-4">
              <h1 className="text-balance text-4xl font-semibold leading-tight text-slate-900 dark:text-white sm:text-5xl">
                Your cross-platform playtime, perfectly organized.
              </h1>
              <p className="max-w-xl text-lg text-slate-600 dark:text-slate-300">
                Connect your gaming accounts and keep a living history of your
                sessions, streaks, and favorite titles across platforms.
              </p>
            </div>
          </section>

          <section className="flex items-center justify-center">
            <div className="w-full max-w-md rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/90 dark:bg-slate-900/70 p-6 shadow-[0_25px_80px_rgba(15,23,42,0.65)] sm:p-8">
              {isLoggedIn ? (
                <div className="space-y-5 text-center">
                  <p className="text-xs font-semibold uppercase tracking-[0.4em] text-emerald-700 dark:text-emerald-300">
                    Signed in
                  </p>
                  <div className="space-y-2">
                    <h2 className="text-2xl font-semibold text-slate-900 dark:text-white">
                      Welcome back
                    </h2>
                    <p className="text-sm text-slate-600 dark:text-slate-300">
                      Logged in as{" "}
                      <span className="font-semibold text-emerald-700 dark:text-emerald-200">
                        {profileLabel}
                      </span>
                    </p>
                  </div>
                  <button
                    type="button"
                    onClick={onDashboardRedirect}
                    className="w-full rounded-xl border border-emerald-400/60 bg-emerald-400/10 px-6 py-3 text-sm font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-100 transition hover:border-emerald-300 hover:text-emerald-50"
                  >
                    Open dashboard
                  </button>
                  <button
                    type="button"
                    onClick={onLogout}
                    className="w-full rounded-xl border border-rose-500/60 px-6 py-3 text-sm font-semibold uppercase tracking-[0.3em] text-rose-700 dark:text-rose-200 transition hover:border-rose-400 hover:text-rose-700 dark:hover:text-rose-100"
                  >
                    Logout
                  </button>
                </div>
              ) : (
                <form className="space-y-5" onSubmit={handleSubmit}>
                  <label className="block text-sm font-medium text-slate-700 dark:text-slate-200">
                    Email address
                    <input
                      type="email"
                      placeholder="you@example.com"
                      value={email}
                      onChange={(event) => setEmail(event.target.value)}
                      className="mt-2 w-full rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-4 py-3 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                    />
                  </label>
                  <label className="block text-sm font-medium text-slate-700 dark:text-slate-200">
                    Password
                    <input
                      type="password"
                      placeholder="••••••••"
                      value={password}
                      onChange={(event) => setPassword(event.target.value)}
                      className="mt-2 w-full rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-4 py-3 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
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

              <div className="mt-6 text-center text-xs text-slate-500 dark:text-slate-400">
                {isLoggedIn ? (
                  <p className="text-emerald-700 dark:text-emerald-300">
                    Session active.
                  </p>
                ) : error ? (
                  <p className="text-rose-700 dark:text-rose-300">{error}</p>
                ) : status === "success" ? (
                  <p className="text-emerald-700 dark:text-emerald-300">
                    Signed in successfully.
                  </p>
                ) : (
                  <p>Need access? Ask an admin to invite you.</p>
                )}
              </div>
            </div>
          </section>
        </div>

        <section className="mt-12 space-y-6">
          <div>
            <h2 className="text-2xl font-semibold text-slate-900 dark:text-white">
              Everything in one place
            </h2>
            <p className="mt-3 text-sm text-slate-600 dark:text-slate-300">
              Keep your play history consistent, discover trends, and set goals
              with a dashboard built for long-term tracking.
            </p>
          </div>

          <div className="grid gap-3 sm:grid-cols-3">
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
                className="flex items-center gap-3 rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/60 p-4 text-sm text-slate-700 dark:text-slate-200"
              >
                <span className="flex h-9 w-9 items-center justify-center rounded-full bg-emerald-400/10 text-emerald-700 dark:text-emerald-300">
                  {feature.icon}
                </span>
                <span>{feature.label}</span>
              </div>
            ))}
          </div>

          <div>
            <h3 className="text-xl font-semibold text-slate-900 dark:text-white">
              Interface preview
            </h3>
            <p className="mt-2 text-sm text-slate-600 dark:text-slate-300">
              Rendered sections powered by prepared sample data.
            </p>
          </div>

          <StatsRecapSection
            title="Sessions recap"
            subtitle="Preview"
            summary={previewRecapSummary}
            platforms={previewRecapPlatforms}
            topGames={previewRecapTopGames}
            colors={previewPlatformColors}
          />

          <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
                  Most popular
                </p>
                <h3 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
                  Top 5 games from recap
                </h3>
              </div>
              <div className="text-xs font-semibold uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                Jan 1 - Dec 31
              </div>
            </div>
            <div className="mt-5 grid gap-3 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-5">
              {previewRecapCards.map((game) => (
                <div key={game.gameId} className="group text-left">
                  <div className="w-full">
                    <div className="overflow-hidden rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50">
                      <div className={`aspect-[2/3] w-full ${game.artClass}`} />
                    </div>
                    <div className="mt-3 grid grid-cols-[1fr_auto] items-start gap-3 text-sm">
                      <span className="min-w-0 line-clamp-2 font-semibold leading-snug text-slate-900 dark:text-white">
                        {game.title}
                      </span>
                      <span className="text-xs font-semibold text-emerald-700 dark:text-emerald-200">
                        {game.hours}
                      </span>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </section>
      </div>
    </main>
  );
};
