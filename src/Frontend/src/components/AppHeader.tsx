import type { Theme } from "@/lib/theme";
import type { GameSearchResponse } from "@/models/games";
import { GameSearchBox } from "./GameSearchBox";
import { ThemeToggle } from "./ThemeToggle";

interface AppHeaderProps {
  title: string;
  navItems: ReadonlyArray<{ label: string; path: string }>;
  path: string;
  onNavigate: (path: string) => void;
  onLogout: () => void;
  theme: Theme;
  onToggleTheme: () => void;
  search: (path: string, signal: AbortSignal) => Promise<GameSearchResponse>;
}

export function AppHeader({ title, navItems, path, onNavigate, onLogout, theme, onToggleTheme, search }: AppHeaderProps) {
  return (
    <header className="flex flex-wrap items-center gap-3 rounded-3xl border border-slate-200 bg-white/90 px-4 py-3 dark:border-slate-800 dark:bg-slate-900/70 sm:px-6 sm:py-4 lg:flex-nowrap">
      <div className="min-w-0 shrink-0">
        <p className="text-xs font-semibold uppercase tracking-[0.5em] text-emerald-700 dark:text-emerald-300">Playdex</p>
        <h1 className="truncate text-2xl font-semibold text-slate-900 dark:text-white">{title}</h1>
      </div>
      <nav
        data-header-nav
        className="order-3 flex w-full flex-wrap items-center gap-2 text-sm lg:order-none lg:ml-auto lg:w-auto lg:flex-nowrap"
      >
        {navItems.map((item) => (
          <button
            key={item.path}
            type="button"
            onClick={() => onNavigate(item.path)}
            className={
              path === item.path
                ? "rounded-full px-3 py-2 text-xs font-semibold uppercase tracking-[0.3em] transition bg-emerald-300 text-slate-900 dark:bg-emerald-400"
                : "rounded-full px-3 py-2 text-xs font-semibold uppercase tracking-[0.3em] transition border border-slate-300 dark:border-slate-700 text-slate-700 dark:text-slate-200 hover:border-emerald-400/60"
            }
          >
            {item.label}
          </button>
        ))}
      </nav>
      <div data-header-utilities className="ml-auto flex shrink-0 items-center gap-2 lg:ml-0">
        <div data-header-search className="w-32 shrink-0 sm:w-36 xl:w-40">
          <GameSearchBox search={search} onNavigate={onNavigate} />
        </div>
        <ThemeToggle theme={theme} onToggle={onToggleTheme} className="shrink-0" />
        <button
          type="button"
          aria-label="Log out"
          title="Log out"
          onClick={onLogout}
          className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-full border border-rose-500/60 text-rose-700 transition hover:border-rose-400 hover:bg-rose-50 hover:text-rose-800 dark:text-rose-200 dark:hover:bg-rose-950/40 dark:hover:text-rose-100"
        >
          <svg aria-hidden="true" viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="1.8">
            <path d="M10 17l5-5-5-5M15 12H3M21 19V5a2 2 0 0 0-2-2h-6" strokeLinecap="round" strokeLinejoin="round"></path>
          </svg>
        </button>
      </div>
    </header>
  );
}
