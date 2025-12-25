import type { FormEvent } from "react";
import { useEffect, useMemo, useState } from "react";

export const App = () => {
  const [path, setPath] = useState(() => window.location.pathname);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [status, setStatus] = useState<"idle" | "loading" | "success">("idle");
  const [error, setError] = useState("");
  const [helloEmail, setHelloEmail] = useState("");
  const [playniteKey, setPlayniteKey] = useState("plx_");
  const [providerStatus, setProviderStatus] = useState(() => [
    { id: "steam", name: "Steam", account: "7656119••••", status: "active" },
    { id: "psn", name: "PlayStation", account: "ivk_psn", status: "paused" },
    { id: "playnite", name: "Playnite", account: "Local", status: "active" },
  ]);

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

  const generatePlayniteKey = () => {
    const bytes = new Uint8Array(12);
    if (globalThis.crypto?.getRandomValues) {
      globalThis.crypto.getRandomValues(bytes);
    } else {
      for (let index = 0; index < bytes.length; index += 1) {
        bytes[index] = Math.floor(Math.random() * 256);
      }
    }
    const token = Array.from(bytes)
      .map((byte) => byte.toString(16).padStart(2, "0"))
      .join("");
    setPlayniteKey(`plx_${token}`);
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError("");
    setStatus("loading");

    try {
      const response = await fetch("/api/login", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify({ email, password }),
      });

      const payload = (await response.json()) as {
        data: { userId: number; email: string; token: string } | null;
        error: { error: string } | null;
      };

      if (!response.ok || payload.error) {
        setError(payload.error?.error ?? "Login failed.");
        setStatus("idle");
        return;
      }

      if (!payload.data?.token) {
        setError("Login failed.");
        setStatus("idle");
        return;
      }

      localStorage.setItem("playdex.token", payload.data.token);
      setStatus("success");
      window.location.assign("/hello");
    } catch (err) {
      setError("Login failed.");
      setStatus("idle");
    }
  };

  useEffect(() => {
    if (path !== "/hello") {
      return;
    }

    const token = localStorage.getItem("playdex.token");
    if (!token) {
      setHelloEmail("");
      setError("Please log in first.");
      return;
    }

    const loadProfile = async () => {
      try {
        const response = await fetch("/api/me", {
          headers: {
            Authorization: `Bearer ${token}`,
          },
        });
        const payload = (await response.json()) as {
          data: { userId: number; email: string } | null;
          error: { error: string } | null;
        };

        if (!response.ok || payload.error || !payload.data) {
          setError(payload.error?.error ?? "Unable to load profile.");
          return;
        }

        setHelloEmail(payload.data.email);
      } catch (err) {
        setError("Unable to load profile.");
      }
    };

    loadProfile();
  }, [path]);

  useEffect(() => {
    const handlePopState = () => setPath(window.location.pathname);
    window.addEventListener("popstate", handlePopState);
    return () => window.removeEventListener("popstate", handlePopState);
  }, []);

  const background = (
    <div className="pointer-events-none absolute inset-0">
      <div className="absolute -left-24 -top-24 h-72 w-72 rounded-full bg-emerald-400/20 blur-3xl" />
      <div className="absolute right-0 top-1/3 h-96 w-96 rounded-full bg-cyan-400/15 blur-3xl" />
      <div className="absolute bottom-0 left-1/3 h-64 w-64 rounded-full bg-amber-400/10 blur-3xl" />
    </div>
  );

  if (path === "/hello") {
    return (
      <main className="relative min-h-screen overflow-hidden bg-slate-950 text-slate-100">
        {background}

        <div className="relative mx-auto flex min-h-screen w-full max-w-4xl flex-col items-center justify-center gap-6 px-6 py-16 text-center">
          <p className="text-xs font-semibold uppercase tracking-[0.5em] text-emerald-300">
            Playdex
          </p>
          <h1 className="text-4xl font-semibold text-white sm:text-5xl">
            Hello{helloEmail ? `, ${helloEmail}` : ""}!
          </h1>
          <p className="max-w-xl text-sm text-slate-300">
            {error
              ? error
              : "You're signed in. We'll route you to your dashboard soon."}
          </p>
          <button
            type="button"
            onClick={() => {
              localStorage.removeItem("playdex.token");
              window.location.assign("/");
            }}
            className="rounded-full border border-slate-700 px-5 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-200 transition hover:border-slate-500"
          >
            Sign out
          </button>
        </div>
      </main>
    );
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
            </nav>
          </header>

          <section className="mt-10 grid gap-6 lg:grid-cols-[1.1fr_0.9fr]">
            <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-6">
              <div className="flex items-center justify-between">
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
                    Current session
                  </p>
                  <h2 className="text-2xl font-semibold text-white">
                    Hades II
                  </h2>
                </div>
                <span className="rounded-full border border-emerald-400/40 px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.3em] text-emerald-200">
                  Live
                </span>
              </div>
              <div className="mt-6 grid gap-4 sm:grid-cols-3">
                {[
                  { label: "Platform", value: "Steam Deck" },
                  { label: "Started", value: "8:41 PM" },
                  { label: "Elapsed", value: "01:12:09" },
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
              <div className="mt-6 flex flex-wrap gap-3 text-xs text-slate-300">
                <span className="rounded-full border border-slate-700 px-3 py-1">
                  Focus: Story run
                </span>
                <span className="rounded-full border border-slate-700 px-3 py-1">
                  Mood: Chill grind
                </span>
                <span className="rounded-full border border-slate-700 px-3 py-1">
                  Goal: 2 more boss clears
                </span>
              </div>
            </div>

            <div className="space-y-6">
              <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-6">
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
                      Today sessions
                    </p>
                    <h2 className="text-xl font-semibold text-white">
                      3 sessions logged
                    </h2>
                  </div>
                  <button
                    type="button"
                    className="rounded-full border border-slate-700 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-200 transition hover:border-emerald-400/60"
                  >
                    View all
                  </button>
                </div>
                <div className="mt-5 space-y-4">
                  {[
                    {
                      title: "Helldivers 2",
                      time: "45 min",
                      platform: "PS5",
                    },
                    {
                      title: "Stardew Valley",
                      time: "30 min",
                      platform: "Switch",
                    },
                    {
                      title: "Hades II",
                      time: "1h 12m",
                      platform: "Steam",
                    },
                  ].map((session) => (
                    <div
                      key={session.title}
                      className="flex items-center justify-between rounded-2xl border border-slate-800 bg-slate-950/50 p-4"
                    >
                      <div>
                        <p className="text-sm font-semibold text-white">
                          {session.title}
                        </p>
                        <p className="text-xs text-slate-400">
                          {session.platform}
                        </p>
                      </div>
                      <p className="text-sm font-semibold text-emerald-200">
                        {session.time}
                      </p>
                    </div>
                  ))}
                </div>
              </div>

              <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-6">
                <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
                  Monthly stats
                </p>
                <h2 className="mt-2 text-xl font-semibold text-white">
                  April highlights
                </h2>
                <div className="mt-5 grid gap-4 sm:grid-cols-3">
                  {[
                    { label: "Hours played", value: "42.8" },
                    { label: "Sessions", value: "28" },
                    { label: "Top title", value: "Helldivers 2" },
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
                <div className="mt-5 rounded-2xl border border-dashed border-slate-700 bg-slate-950/40 p-4 text-xs text-slate-400">
                  Trendline placeholder for monthly activity chart.
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
                        className="rounded-full border border-emerald-400/60 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-emerald-200 transition hover:border-emerald-300"
                      >
                        Generate key
                      </button>
                      <span className="rounded-full border border-slate-700 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-200">
                        {playniteKey}
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
              <div className="mt-6 space-y-4">
                {providerStatus.map((provider) => (
                  <div
                    key={provider.id}
                    className="flex flex-wrap items-center justify-between gap-4 rounded-2xl border border-slate-800 bg-slate-950/50 p-4"
                  >
                    <div>
                      <p className="text-sm font-semibold text-white">
                        {provider.name}
                      </p>
                      <p className="text-xs text-slate-400">
                        {provider.account}
                      </p>
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
                ))}
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
            </nav>
          </header>

          <section className="mt-10 rounded-3xl border border-slate-800 bg-slate-900/60 p-6">
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
                className="rounded-full border border-slate-700 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-200"
              >
                Export CSV
              </button>
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

            <div className="mt-6 overflow-hidden rounded-2xl border border-slate-800">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-950/70 text-xs uppercase tracking-[0.3em] text-slate-400">
                  <tr>
                    <th className="px-4 py-3">Title</th>
                    <th className="px-4 py-3">Platform</th>
                    <th className="px-4 py-3">Date</th>
                    <th className="px-4 py-3 text-right">Duration</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-800 bg-slate-900/60">
                  {[
                    {
                      title: "Helldivers 2",
                      platform: "PS5",
                      date: "Apr 14",
                      duration: "45m",
                    },
                    {
                      title: "Hades II",
                      platform: "Steam",
                      date: "Apr 14",
                      duration: "1h 12m",
                    },
                    {
                      title: "Stardew Valley",
                      platform: "Switch",
                      date: "Apr 13",
                      duration: "30m",
                    },
                    {
                      title: "Sea of Stars",
                      platform: "Playnite",
                      date: "Apr 12",
                      duration: "2h 05m",
                    },
                    {
                      title: "Baldur's Gate 3",
                      platform: "Steam",
                      date: "Apr 11",
                      duration: "1h 38m",
                    },
                  ].map((session) => (
                    <tr key={`${session.title}-${session.date}`}>
                      <td className="px-4 py-3 font-semibold text-white">
                        {session.title}
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
                  ))}
                </tbody>
              </table>
            </div>

            <div className="mt-6 flex flex-wrap items-center justify-between gap-4 text-xs text-slate-300">
              <p>Showing 1-5 of 48 sessions</p>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  className="rounded-full border border-slate-700 px-3 py-2 font-semibold uppercase tracking-[0.3em] text-slate-200"
                >
                  Prev
                </button>
                {[1, 2, 3, 4].map((page) => (
                  <button
                    key={page}
                    type="button"
                    className={`rounded-full px-3 py-2 font-semibold uppercase tracking-[0.3em] ${
                      page === 1
                        ? "bg-emerald-400 text-slate-900"
                        : "border border-slate-700 text-slate-200"
                    }`}
                  >
                    {page}
                  </button>
                ))}
                <button
                  type="button"
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

              <div className="mt-6 text-center text-xs text-slate-400">
                {error ? (
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
