export const App = () => {
  return (
    <main className="relative min-h-screen overflow-hidden bg-slate-950 text-slate-100">
      <div className="pointer-events-none absolute inset-0">
        <div className="absolute -left-24 -top-24 h-72 w-72 rounded-full bg-emerald-400/20 blur-3xl" />
        <div className="absolute right-0 top-1/3 h-96 w-96 rounded-full bg-cyan-400/15 blur-3xl" />
        <div className="absolute bottom-0 left-1/3 h-64 w-64 rounded-full bg-amber-400/10 blur-3xl" />
      </div>

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
              <div className="space-y-2">
                <h2 className="text-2xl font-semibold text-white">Welcome back</h2>
                <p className="text-sm text-slate-400">
                  Use your Playdex account to continue.
                </p>
              </div>

              <form className="mt-8 space-y-5">
                <label className="block text-sm font-medium text-slate-200">
                  Email address
                  <input
                    type="email"
                    placeholder="you@example.com"
                    className="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950/80 px-4 py-3 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                  />
                </label>
                <label className="block text-sm font-medium text-slate-200">
                  Password
                  <input
                    type="password"
                    placeholder="••••••••"
                    className="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950/80 px-4 py-3 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                  />
                </label>

                <button
                  type="button"
                  className="w-full rounded-xl bg-emerald-400 px-6 py-3 text-sm font-semibold text-slate-900 transition hover:bg-emerald-300"
                >
                  Log in
                </button>
              </form>

              <p className="mt-6 text-center text-xs text-slate-400">
                Need access? Ask an admin to invite you.
              </p>
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
              "Track Steam and PSN playtime",
              "Automatic polling with scheduled refresh",
              "Fast search across your library",
            ].map((feature) => (
              <div
                key={feature}
                className="rounded-2xl border border-slate-800 bg-slate-950/60 p-4 text-sm text-slate-200"
              >
                {feature}
              </div>
            ))}
          </div>
        </section>
      </div>
    </main>
  );
};
