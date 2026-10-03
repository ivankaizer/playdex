export function Background() {
  return (
    <div className="pointer-events-none absolute inset-0">
      <div className="absolute -left-24 -top-24 h-72 w-72 rounded-full bg-emerald-200/60 dark:bg-emerald-400/20 blur-3xl"></div>
      <div className="absolute right-0 top-1/3 h-96 w-96 rounded-full bg-cyan-200/50 dark:bg-cyan-400/15 blur-3xl"></div>
      <div className="absolute bottom-0 left-1/3 h-64 w-64 rounded-full bg-amber-200/40 dark:bg-amber-400/10 blur-3xl"></div>
    </div>
  );
}
