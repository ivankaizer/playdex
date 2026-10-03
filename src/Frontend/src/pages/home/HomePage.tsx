import { useEffect, useRef, useState, type FormEvent } from "react";
import { AppFooter } from "@/components/AppFooter";
import { Background } from "@/components/Background";
import { StatsRecapSection, type RecapPlatformItem, type RecapSummaryItem, type RecapTopGame } from "@/components/StatsRecapSection";
import { ThemeToggle } from "@/components/ThemeToggle";
import { apiRequest, errorMessage } from "@/lib/api";
import { authSession, type LoginRequest, type LoginResponse, type MeResponse } from "@/lib/auth";
import { logout } from "@/lib/navigation";
import { useTheme } from "@/lib/theme";
import {
  beginLogin,
  beginProfile,
  completeLogin,
  completeProfile,
  failLogin,
  failProfile,
  initialHomeState,
  isLoginDisabled,
  profileLabel,
  type HomeState,
} from "./homeState";

const previewPlatformColors: readonly string[] = ["#34d399", "#38bdf8", "#f59e0b", "#a78bfa"];

const previewRecapSummary: readonly RecapSummaryItem[] = [
  { label: "Hours played", value: "124.8h" },
  { label: "Sessions", value: "93" },
  { label: "Top title", value: "Hades II" },
];

const previewRecapPlatforms: readonly RecapPlatformItem[] = [
  { key: "steam", label: "Steam", value: 52, hours: "64.9h" },
  { key: "ps5", label: "PlayStation", value: 30, hours: "37.4h" },
  { key: "xbox-series-xs", label: "Xbox", value: 12, hours: "14.9h" },
  { key: "switch-1", label: "Switch", value: 6, hours: "7.6h" },
];

const previewRecapTopGames: readonly RecapTopGame[] = [
  { gameId: "hades-2", title: "Hades II", hours: "26.4h" },
  { gameId: "bg3", title: "Baldur's Gate 3", hours: "18.2h" },
  { gameId: "re4", title: "Resident Evil 4", hours: "14.7h" },
  { gameId: "helldivers-2", title: "Helldivers 2", hours: "12.6h" },
  { gameId: "fh5", title: "Forza Horizon 5", hours: "10.8h" },
];

interface PreviewCard {
  gameId: string;
  title: string;
  hours: string;
  artClass: string;
}

const previewRecapCards: readonly PreviewCard[] = [
  { gameId: "hades-2", title: "Hades II", hours: "26.4h", artClass: "bg-gradient-to-br from-rose-400/60 via-fuchsia-500/40 to-slate-950/70" },
  { gameId: "bg3", title: "Baldur's Gate 3", hours: "18.2h", artClass: "bg-gradient-to-br from-amber-300/60 via-orange-500/40 to-slate-950/70" },
  { gameId: "re4", title: "Resident Evil 4", hours: "14.7h", artClass: "bg-gradient-to-br from-emerald-300/60 via-teal-500/40 to-slate-950/70" },
  { gameId: "helldivers-2", title: "Helldivers 2", hours: "12.6h", artClass: "bg-gradient-to-br from-sky-300/60 via-cyan-500/40 to-slate-950/70" },
  { gameId: "fh5", title: "Forza Horizon 5", hours: "10.8h", artClass: "bg-gradient-to-br from-violet-300/60 via-indigo-500/40 to-slate-950/70" },
];

const inputClass =
  "mt-2 w-full rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-4 py-3 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30";

const featureCardClass =
  "flex items-center gap-3 rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/60 p-4 text-sm text-slate-700 dark:text-slate-200";

const featureIconClass =
  "flex h-9 w-9 items-center justify-center rounded-full bg-emerald-400/10 text-emerald-700 dark:text-emerald-300";

export function HomePage() {
  const { theme, toggleTheme } = useTheme();
  // Mirrors App.razor's HomeSessionState: resolved once when the page is created.
  const [isLoggedIn] = useState(() => authSession.isLoggedIn());
  const [state, setState] = useState<HomeState>(() =>
    beginProfile(initialHomeState, isLoggedIn, authSession.getEmail()),
  );
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const lifetime = useRef<AbortController | null>(null);
  const shouldLoadProfileRef = useRef(state.isProfileLoading);

  useEffect(() => {
    const controller = new AbortController();
    lifetime.current = controller;

    if (shouldLoadProfileRef.current) {
      void (async () => {
        try {
          const profile = await apiRequest<MeResponse>("/me", { signal: controller.signal });
          if (controller.signal.aborted) return;
          authSession.storeProfile(profile);
          setState((current) => completeProfile(current, profile));
        } catch {
          if (controller.signal.aborted) return;
          setState((current) => failProfile(current));
        }
      })();
    }

    return () => controller.abort();
  }, []);

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const signal = lifetime.current?.signal;
    setState((current) => beginLogin(current));
    try {
      const request: LoginRequest = { email, password };
      const response = await apiRequest<LoginResponse>("/login", {
        method: "POST",
        body: request,
        authenticated: false,
        signal,
      });
      if (signal?.aborted) return;
      authSession.storeLogin(response);
      setState((current) => completeLogin(current, response));
      window.location.assign("/dashboard");
    } catch (error) {
      if (signal?.aborted) return;
      setState((current) => failLogin(current, errorMessage(error)));
    }
  };

  const openDashboard = () => window.location.assign("/dashboard");

  return (
    <main className="relative min-h-screen overflow-hidden bg-slate-200 dark:bg-slate-950 text-slate-900 dark:text-slate-100">
      <Background />

      <div className="relative mx-auto w-full max-w-6xl px-4 py-12 sm:px-6 sm:py-16">
        <div className="flex justify-end mb-6">
          <ThemeToggle theme={theme} onToggle={toggleTheme} />
        </div>
        <div className="grid gap-12 lg:grid-cols-[1.1fr_0.9fr]">
          <section className="flex flex-col justify-center gap-8">
            <div className="flex items-center gap-3">
              <span className="inline-flex h-3 w-3 rounded-full bg-emerald-400 shadow-[0_0_14px_rgba(52,211,153,0.8)]"></span>
              <p className="text-xs font-semibold uppercase tracking-[0.5em] text-emerald-700 dark:text-emerald-300">Playdex</p>
            </div>

            <div className="space-y-4">
              <h1 className="text-balance text-4xl font-semibold leading-tight text-slate-900 dark:text-white sm:text-5xl">
                Your cross-platform playtime, perfectly organized.
              </h1>
              <p className="max-w-xl text-lg text-slate-600 dark:text-slate-300">
                Connect your gaming accounts and keep a living history of your sessions, streaks, and favorite titles across
                platforms.
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
                    <h2 className="text-2xl font-semibold text-slate-900 dark:text-white">Welcome back</h2>
                    <p className="text-sm text-slate-600 dark:text-slate-300">
                      Logged in as{" "}
                      <span className="font-semibold text-emerald-700 dark:text-emerald-200">{profileLabel(state)}</span>
                    </p>
                  </div>
                  <button
                    type="button"
                    onClick={openDashboard}
                    className="w-full rounded-xl border border-emerald-400/60 bg-emerald-400/10 px-6 py-3 text-sm font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-100 transition hover:border-emerald-300 hover:text-emerald-50"
                  >
                    Open dashboard
                  </button>
                  <button
                    type="button"
                    onClick={logout}
                    className="w-full rounded-xl border border-rose-500/60 px-6 py-3 text-sm font-semibold uppercase tracking-[0.3em] text-rose-700 dark:text-rose-200 transition hover:border-rose-400 hover:text-rose-700 dark:hover:text-rose-100"
                  >
                    Logout
                  </button>
                </div>
              ) : (
                <form className="space-y-5" onSubmit={(event) => void handleSubmit(event)}>
                  <label className="block text-sm font-medium text-slate-700 dark:text-slate-200">
                    Email address
                    <input
                      type="email"
                      placeholder="you@example.com"
                      value={email}
                      onChange={(event) => setEmail(event.target.value)}
                      className={inputClass}
                    />
                  </label>
                  <label className="block text-sm font-medium text-slate-700 dark:text-slate-200">
                    Password
                    <input
                      type="password"
                      placeholder="••••••••"
                      value={password}
                      onChange={(event) => setPassword(event.target.value)}
                      className={inputClass}
                    />
                  </label>

                  <button
                    type="submit"
                    disabled={isLoginDisabled(state)}
                    className="w-full rounded-xl bg-emerald-400 px-6 py-3 text-sm font-semibold text-slate-900 transition hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-emerald-300/60"
                  >
                    {state.loginStatus === "loading" ? "Signing in..." : "Log in"}
                  </button>
                </form>
              )}

              <div className="mt-6 text-center text-xs text-slate-500 dark:text-slate-400">
                {isLoggedIn ? (
                  <p className="text-emerald-700 dark:text-emerald-300">Session active.</p>
                ) : state.error.length > 0 ? (
                  <p className="text-rose-700 dark:text-rose-300">{state.error}</p>
                ) : state.loginStatus === "success" ? (
                  <p className="text-emerald-700 dark:text-emerald-300">Signed in successfully.</p>
                ) : (
                  <p>Need access? Ask an admin to invite you.</p>
                )}
              </div>
            </div>
          </section>
        </div>

        <section className="mt-12 space-y-6">
          <div>
            <h2 className="text-2xl font-semibold text-slate-900 dark:text-white">Everything in one place</h2>
            <p className="mt-3 text-sm text-slate-600 dark:text-slate-300">
              Keep your play history consistent, discover trends, and set goals with a dashboard built for long-term tracking.
            </p>
          </div>

          <div className="grid gap-3 sm:grid-cols-3">
            <div className={featureCardClass}>
              <span className={featureIconClass}>
                <svg aria-hidden="true" viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.5">
                  <circle cx="12" cy="12" r="8"></circle>
                  <path strokeLinecap="round" d="M12 8v4l3 2"></path>
                </svg>
              </span>
              <span>Track Steam and PSN playtime</span>
            </div>
            <div className={featureCardClass}>
              <span className={featureIconClass}>
                <svg aria-hidden="true" viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.5">
                  <path strokeLinecap="round" d="M20 8v-3m0 0h-3m3 0-3.5 3.5"></path>
                  <path strokeLinecap="round" d="M4 16v3m0 0h3m-3 0 3.5-3.5"></path>
                  <path strokeLinecap="round" d="M6.5 6.5a8 8 0 0 1 11 0M6.5 17.5a8 8 0 0 0 11 0"></path>
                </svg>
              </span>
              <span>Automatic polling with scheduled refresh</span>
            </div>
            <div className={featureCardClass}>
              <span className={featureIconClass}>
                <svg aria-hidden="true" viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.5">
                  <circle cx="11" cy="11" r="6.5"></circle>
                  <path strokeLinecap="round" d="m16 16 4 4"></path>
                </svg>
              </span>
              <span>Fast search across your library</span>
            </div>
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
                <h3 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">Top 5 games from recap</h3>
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
                      <div className={`aspect-[2/3] w-full ${game.artClass}`}></div>
                    </div>
                    <div className="mt-3 grid grid-cols-[1fr_auto] items-start gap-3 text-sm">
                      <span className="min-w-0 line-clamp-2 font-semibold leading-snug text-slate-900 dark:text-white">
                        {game.title}
                      </span>
                      <span className="text-xs font-semibold text-emerald-700 dark:text-emerald-200">{game.hours}</span>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </section>

        <AppFooter />
      </div>
    </main>
  );
}
