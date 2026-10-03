export function AppFooter() {
  return (
    <footer className="mt-10 flex flex-wrap items-center justify-between gap-4 rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/90 dark:bg-slate-900/70 px-4 py-3 sm:px-6 sm:py-4 md:flex-nowrap">
      <div className="min-w-0 flex-1">
        <p className="text-xs font-semibold uppercase tracking-[0.5em] text-emerald-700 dark:text-emerald-300">Playdex</p>
        <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">Playtime tracking for connected libraries.</p>
      </div>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-xs font-semibold uppercase tracking-[0.25em] text-slate-500 dark:text-slate-400">
        <span>© {new Date().getFullYear()} Playdex</span>
        <span className="hidden h-1 w-1 rounded-full bg-emerald-400 sm:inline-flex"></span>
        <span>Private dashboard</span>
      </div>
    </footer>
  );
}
