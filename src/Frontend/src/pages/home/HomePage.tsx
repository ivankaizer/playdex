import { useEffect, useRef, useState, type FormEvent } from "react";
import { AppFooter } from "@/components/AppFooter";
import { PlatformChips } from "@/components/PlatformChips";
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
        <header className="flex flex-wrap items-center gap-4 rounded-[28px] border border-[color:var(--card-border)] bg-surface/60 py-3.5 pl-7 pr-4 sm:rounded-full">
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
            <PlatformChips />
          </div>

          <div className="min-w-0">
            <div className="flex flex-col gap-5 rounded-card border border-[color:var(--card-border)] bg-surface/85 p-6 shadow-pop sm:p-9">
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

        <AppFooter />
      </div>
    </main>
  );
}
