const features = [
  "Track Steam and PSN playtime",
  "Automatic polling with scheduled refresh",
  "Fast search across your library",
  "Metrics and health endpoints",
];

export const App = () => {
  return (
    <main className="min-h-screen bg-slate-950 text-slate-100">
      <div className="mx-auto flex w-full max-w-5xl flex-col gap-12 px-6 py-16">
        <header className="space-y-4">
          <p className="text-sm font-semibold uppercase tracking-[0.4em] text-emerald-300">
            Playdex
          </p>
          <h1 className="text-balance text-4xl font-semibold leading-tight text-white sm:text-5xl">
            Your cross-platform playtime, perfectly organized.
          </h1>
          <p className="max-w-2xl text-lg text-slate-300">
            Connect your gaming accounts and keep a living history of your
            sessions, streaks, and favorite titles across platforms.
          </p>
          <div className="flex flex-wrap gap-4">
            <button className="rounded-full bg-emerald-400 px-6 py-3 text-sm font-semibold text-slate-900 transition hover:bg-emerald-300">
              Connect accounts
            </button>
            <button className="rounded-full border border-slate-700 px-6 py-3 text-sm font-semibold text-slate-100 transition hover:border-slate-500">
              View demo
            </button>
          </div>
        </header>

        <section className="grid gap-6 rounded-3xl border border-slate-800 bg-slate-900/40 p-8 sm:grid-cols-2">
          <div className="space-y-4">
            <h2 className="text-2xl font-semibold">Everything in one place</h2>
            <p className="text-slate-300">
              Keep your play history consistent, discover trends, and set goals
              with a dashboard built for long-term tracking.
            </p>
          </div>
          <ul className="space-y-3 text-sm text-slate-300">
            {features.map((feature) => (
              <li key={feature} className="flex items-center gap-3">
                <span className="inline-flex h-2 w-2 rounded-full bg-emerald-400" />
                {feature}
              </li>
            ))}
          </ul>
        </section>
      </div>
    </main>
  );
};
