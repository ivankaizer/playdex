import { useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { AppFooter } from "@/components/AppFooter";
import { ThemeToggle } from "@/components/ThemeToggle";
import { BarChart, CardHeading, Cover, Donut, LegendRow, StatTile, type BarDatum, type DonutSegment } from "@/components/ui";
import { apiRequest, errorMessage } from "@/lib/api";
import { authSession, type LoginRequest, type LoginResponse, type MeResponse } from "@/lib/auth";
import { logout } from "@/lib/navigation";
import { platformColor } from "@/lib/platforms";
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

/*
 * Landing-page preview of the recap. These are the page's illustrative sample
 * figures (shown under a "Preview" kicker), not the visitor's data.
 */
const previewKpis: ReadonlyArray<{ label: string; value: string }> = [
  { label: "Hours played", value: "124.8h" },
  { label: "Sessions", value: "93" },
  { label: "Top title", value: "Hades II" },
];

const previewTotalHours = "124.8h";

const previewPlatforms: ReadonlyArray<{ key: string; label: string; value: number; hours: string }> = [
  { key: "steam", label: "Steam", value: 52, hours: "64.9h" },
  { key: "ps5", label: "PlayStation", value: 30, hours: "37.4h" },
  { key: "xbox-series-xs", label: "Xbox", value: 12, hours: "14.9h" },
  { key: "switch-1", label: "Switch", value: 6, hours: "7.6h" },
];

/** Steam library art for the sample games (portrait, 600px wide). */
const steamCover = (appId: number) => `https://cdn.cloudflare.steamstatic.com/steam/apps/${appId}/library_600x900_2x.jpg`;

const previewTopGames: ReadonlyArray<{ gameId: string; title: string; hours: number; cover: string }> = [
  { gameId: "hades-2", title: "Hades II", hours: 26.4, cover: steamCover(1145350) },
  { gameId: "bg3", title: "Baldur's Gate 3", hours: 18.2, cover: steamCover(1086940) },
  { gameId: "re4", title: "Resident Evil 4", hours: 14.7, cover: steamCover(2050650) },
  { gameId: "helldivers-2", title: "Helldivers 2", hours: 12.6, cover: steamCover(553850) },
  { gameId: "fh5", title: "Forza Horizon 5", hours: 10.8, cover: steamCover(1551360) },
];

const coverFallbacks: readonly string[] = [
  "linear-gradient(160deg,#f5a3b5,#e5a8f0 45%,#4a4f63)",
  "linear-gradient(160deg,#f5d98f,#f2bfa4 45%,#4a4f63)",
  "linear-gradient(160deg,#9fe8cf,#a6dcd8 45%,#4a4f63)",
  "linear-gradient(160deg,#a6dcf5,#a3d8ea 45%,#4a4f63)",
  "linear-gradient(160deg,#d3c8f7,#b5b8f2 45%,#4a4f63)",
];

const platformChips: ReadonlyArray<{ key: string; name: string; mark: string }> = [
  { key: "steam", name: "Steam", mark: "S" },
  { key: "psn", name: "PlayStation", mark: "PS" },
  { key: "xbox", name: "Xbox", mark: "X" },
  { key: "switch", name: "Switch", mark: "NS" },
];

const donutSegments: readonly DonutSegment[] = previewPlatforms.map((platform) => ({
  key: platform.key,
  label: platform.label,
  value: platform.value,
  color: platformColor(platform.key, platform.label),
}));

const peakGame = previewTopGames.reduce((best, game) => (game.hours > best.hours ? game : best), previewTopGames[0]);

const topGameBars: readonly BarDatum[] = previewTopGames.map((game) => ({
  key: game.gameId,
  value: game.hours,
  label: game.title,
  title: `${game.title} · ${game.hours}h`,
  highlight: game.gameId === peakGame.gameId,
}));

const iconProps = {
  "aria-hidden": true,
  viewBox: "0 0 24 24",
  className: "h-6 w-6",
  fill: "none",
  stroke: "currentColor",
  strokeWidth: 1.8,
  strokeLinecap: "round",
  strokeLinejoin: "round",
} as const;

const features: ReadonlyArray<{ title: string; body: string; icon: ReactNode }> = [
  {
    title: "Steam, PlayStation and Xbox, synced for you",
    body: "Connect once and every session shows up on its own. No timers to start, nothing to forget.",
    icon: (
      <svg {...iconProps}>
        <path d="M21 12a9 9 0 1 1-2.64-6.36"></path>
        <path d="M21 3v6h-6"></path>
      </svg>
    ),
  },
  {
    title: "Log a whole weekend in one paste",
    body: "Describe what you played or drop a screenshot. AI turns it into sessions you review and accept.",
    icon: (
      <svg {...iconProps}>
        <path d="M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9z"></path>
        <path d="M19 15l.8 2.2L22 18l-2.2.8L19 21l-.8-2.2L16 18l2.2-.8z"></path>
      </svg>
    ),
  },
  {
    title: "Your year in games, on demand",
    body: "Peak hours, longest streak, most-binged title and where the time really went, for any year or range.",
    icon: (
      <svg {...iconProps}>
        <path d="M4 20V10M10 20V4M16 20v-7M22 20H2"></path>
      </svg>
    ),
  },
  {
    title: "Switch, retro, anything else",
    body: "Start a live timer or log past sessions for platforms that don't sync, and they count like the rest.",
    icon: (
      <svg {...iconProps}>
        <circle cx="12" cy="13" r="8"></circle>
        <path d="M12 9v4l2.5 1.5M10 2h4"></path>
      </svg>
    ),
  },
];

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
  const emailInputRef = useRef<HTMLInputElement | null>(null);

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
  const focusLogin = () => emailInputRef.current?.focus();
  const label = profileLabel(state);
  const initial = state.sessionEmail.trim().charAt(0).toUpperCase() || "?";

  return (
    <main className="pdx-page relative px-4 pb-10 pt-6 sm:px-8 sm:pt-8">
      <div className="relative mx-auto flex w-full max-w-[1180px] flex-col gap-7">
        <header className="flex flex-wrap items-center gap-4 rounded-[28px] border border-[color:var(--card-border)] bg-surface/60 py-3.5 pl-7 pr-4 backdrop-blur-[16px] sm:rounded-full">
          <div className="mr-auto flex items-center gap-2.5">
            <span className="h-2.5 w-2.5 rounded-full bg-bar shadow-[0_0_0_4px_rgba(47,194,142,.18)]"></span>
            <span className="text-xs font-semibold uppercase tracking-[0.32em] text-accent">Playdex</span>
          </div>
          <ThemeToggle theme={theme} onToggle={toggleTheme} />
          {isLoggedIn ? (
            <>
              <span className="min-w-0 truncate text-[13px] text-muted">{label}</span>
              <button type="button" onClick={logout} className="pdx-btn-danger !h-[38px] !px-4 !text-[13px]">
                Log out
              </button>
              <button type="button" onClick={openDashboard} className="pdx-btn-primary !h-[38px] !px-[18px] !text-[13px]">
                Open dashboard →
              </button>
            </>
          ) : (
            <button type="button" onClick={focusLogin} className="pdx-btn-outline !h-[38px] !px-[18px] !text-[13px] !text-ink">
              Sign in
            </button>
          )}
        </header>

        <section className="grid items-center gap-12 px-2 pb-6 pt-6 sm:pt-10 lg:grid-cols-[minmax(0,1fr)_minmax(360px,420px)]">
          <div className="flex min-w-0 flex-col gap-7">
            <h1 className="m-0 text-[44px] font-semibold leading-[1.02] tracking-[-0.035em] sm:text-[68px]">
              Your cross-platform playtime, <span className="text-accent">perfectly organized.</span>
            </h1>
            <p className="m-0 max-w-[34em] text-pretty text-[17px] leading-[1.55] text-ink-2 sm:text-[19px]">
              Connect your gaming accounts and keep a living history of your sessions, streaks, and favorite titles across
              platforms.
            </p>
            <ul aria-label="Supported platforms" className="m-0 flex list-none flex-wrap gap-2 p-0">
              {platformChips.map((chip) => (
                <li
                  key={chip.key}
                  className="flex h-9 items-center gap-2 rounded-full border border-[color:var(--card-border)] bg-surface/75 pl-1.5 pr-3.5 text-[13px] font-medium"
                >
                  <span
                    aria-hidden="true"
                    className="grid h-6 w-6 place-items-center rounded-full text-[10px] font-bold text-white"
                    style={{ backgroundColor: platformColor(chip.key, chip.name) }}
                  >
                    {chip.mark}
                  </span>
                  {chip.name}
                </li>
              ))}
            </ul>
          </div>

          <div className="min-w-0">
            <div className="flex flex-col gap-5 rounded-card border border-[color:var(--card-border)] bg-surface/85 p-6 shadow-pop backdrop-blur-[16px] sm:p-9">
              {isLoggedIn ? (
                <>
                  <div className="flex items-center gap-3.5">
                    <div
                      aria-hidden="true"
                      className="grid h-[52px] w-[52px] shrink-0 place-items-center rounded-full bg-mint text-xl font-semibold text-accent"
                    >
                      {initial}
                    </div>
                    <div className="min-w-0">
                      <p className="pdx-kicker">Signed in</p>
                      <h2 className="mt-0.5 text-2xl font-semibold">Welcome back</h2>
                    </div>
                  </div>
                  <p className="text-sm text-muted">
                    Logged in as <span className="break-all font-semibold text-accent">{label}</span>
                  </p>
                  <button type="button" onClick={openDashboard} className="pdx-btn-primary !h-[52px] w-full !text-[15px]">
                    Open dashboard →
                  </button>
                </>
              ) : (
                <form aria-label="Sign in" className="flex flex-col gap-5" onSubmit={(event) => void handleSubmit(event)}>
                  <div>
                    <p className="pdx-kicker">Sign in</p>
                    <h2 className="mt-1 text-2xl font-semibold">Pick up where you left off</h2>
                  </div>
                  <label className="flex flex-col gap-1.5 text-xs font-medium text-muted">
                    Email
                    <input
                      ref={emailInputRef}
                      type="email"
                      autoComplete="email"
                      placeholder="you@example.com"
                      value={email}
                      onChange={(event) => setEmail(event.target.value)}
                      className="pdx-input !h-12 !px-5 !text-[15px]"
                    />
                  </label>
                  <label className="flex flex-col gap-1.5 text-xs font-medium text-muted">
                    Password
                    <input
                      type="password"
                      autoComplete="current-password"
                      placeholder="••••••••"
                      value={password}
                      onChange={(event) => setPassword(event.target.value)}
                      className="pdx-input !h-12 !px-5 !text-[15px]"
                    />
                  </label>
                  <button type="submit" disabled={isLoginDisabled(state)} className="pdx-btn-primary !h-[52px] w-full !text-[15px]">
                    {state.loginStatus === "loading" ? "Signing in..." : "Sign in"}
                  </button>
                </form>
              )}

              <div className="text-[13px]">
                {isLoggedIn ? (
                  <p className="flex items-center gap-2 text-accent">
                    <span aria-hidden="true" className="h-2 w-2 rounded-full bg-bar"></span>
                    Session active.
                  </p>
                ) : state.error.length > 0 ? (
                  <p role="alert" className="text-danger">
                    {state.error}
                  </p>
                ) : state.loginStatus === "success" ? (
                  <p className="text-accent">Signed in successfully.</p>
                ) : (
                  <p className="text-muted">Need access? Ask an admin to invite you.</p>
                )}
              </div>
            </div>
          </div>
        </section>

        <section className="flex flex-col gap-5">
          <div className="px-2">
            <p className="pdx-kicker">Features</p>
            <h2 className="mb-1.5 mt-2 text-[32px] font-semibold tracking-[-0.01em]">Every hour you play, accounted for</h2>
            <p className="m-0 text-[15px] text-muted">
              Automatic where it can be, effortless where it can't. One history across every platform you own.
            </p>
          </div>
          <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(260px,100%),1fr))]">
            {features.map((feature) => (
              <div key={feature.title} className="pdx-card flex flex-col gap-4 !rounded-[28px] !p-7">
                <span className="grid h-[52px] w-[52px] place-items-center rounded-2xl bg-mint text-accent">{feature.icon}</span>
                <h3 className="m-0 text-lg font-semibold leading-[1.3]">{feature.title}</h3>
                <p className="m-0 text-sm leading-normal text-muted">{feature.body}</p>
              </div>
            ))}
          </div>
        </section>

        <section className="pdx-card flex flex-col gap-7">
          <CardHeading
            kicker="Sessions recap · Preview"
            title="A month at a glance"
            actions={
              <span className="rounded-full bg-tile px-3.5 py-1.5 text-xs font-semibold text-ink-2">Sample data</span>
            }
          />
          <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(200px,100%),1fr))]">
            {previewKpis.map((kpi) => (
              <StatTile key={kpi.label} label={kpi.label} value={kpi.value} />
            ))}
          </div>
          <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(320px,100%),1fr))]">
            <div className="pdx-tile flex flex-col gap-6 !p-7">
              <p className="pdx-label">Platform distribution</p>
              <div className="flex flex-wrap items-center gap-8">
                <Donut
                  segments={donutSegments}
                  center={
                    <div className="flex flex-col items-center gap-0.5 leading-[1.1]">
                      <span className="pdx-num text-xl font-semibold">{previewTotalHours}</span>
                      <span className="text-xs text-muted">total</span>
                    </div>
                  }
                />
                <div className="flex min-w-0 flex-[1_1_140px] flex-col gap-3.5">
                  {previewPlatforms.map((platform, index) => (
                    <LegendRow
                      key={platform.key}
                      color={donutSegments[index].color}
                      name={platform.label}
                      pct={`${platform.value}%`}
                      detail={platform.hours}
                    />
                  ))}
                </div>
              </div>
            </div>
            <div className="pdx-tile flex flex-col gap-3.5 !p-7">
              <div className="flex flex-wrap items-baseline justify-between gap-2">
                <span className="pdx-label">Hours by title</span>
                <span className="pdx-num text-[13px] text-muted">
                  Peak {peakGame.title} · {peakGame.hours}h
                </span>
              </div>
              <BarChart data={topGameBars} height={150} gap={10} radius={6} ariaLabel="Hours by title" />
            </div>
          </div>
        </section>

        <section className="pdx-card flex flex-col gap-6">
          <CardHeading
            kicker="Most popular"
            title="Top 5 games from recap"
            actions={<span className="text-xs font-semibold uppercase tracking-[0.2em] text-muted">Jan 1 – Dec 31</span>}
          />
          <ol className="m-0 grid list-none gap-4 p-0 [grid-template-columns:repeat(auto-fit,minmax(min(120px,100%),1fr))]">
            {previewTopGames.map((game, index) => (
              <li key={game.gameId} className="flex min-w-0 flex-col gap-3">
                <div className="relative">
                  <Cover
                    title={game.title}
                    src={game.cover}
                    className="aspect-[2/3] w-full rounded-[22px] shadow-cover"
                    style={{ background: coverFallbacks[index % coverFallbacks.length] }}
                    initialClassName="text-[40px]"
                  />
                  <span className="absolute left-2.5 top-2.5 grid h-8 min-w-8 place-items-center rounded-full bg-white/90 px-2.5 text-[13px] font-bold text-pill-ink">
                    <span className="sr-only">Rank </span>
                    {index + 1}
                  </span>
                </div>
                <div className="flex items-baseline justify-between gap-2">
                  <span className="min-w-0 text-[15px] font-medium leading-[1.3]">{game.title}</span>
                  <span className="pdx-num text-sm font-semibold text-accent">{game.hours}h</span>
                </div>
              </li>
            ))}
          </ol>
        </section>

        <AppFooter />
      </div>
    </main>
  );
}
