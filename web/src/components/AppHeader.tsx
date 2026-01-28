import type { NavItem } from "../types";
import { ThemeToggle } from "./ThemeToggle";

type AppHeaderProps = {
  title: string;
  navItems: NavItem[];
  path: string;
  onNavigate: (path: string) => void;
  onLogout: () => void;
  theme: "light" | "dark";
  onToggleTheme: () => void;
};

export const AppHeader = ({
  title,
  navItems,
  path,
  onNavigate,
  onLogout,
  theme,
  onToggleTheme,
}: AppHeaderProps) => (
  <header className="flex flex-wrap items-center justify-between gap-4 rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/90 dark:bg-slate-900/70 px-4 py-3 sm:px-6 sm:py-4 md:flex-nowrap">
    <div className="min-w-0 flex-1">
      <p className="text-xs font-semibold uppercase tracking-[0.5em] text-emerald-700 dark:text-emerald-300">
        Playdex
      </p>
      <h1 className="truncate text-2xl font-semibold text-slate-900 dark:text-white">{title}</h1>
    </div>
    <nav className="flex flex-wrap items-center gap-3 text-sm md:flex-nowrap">
      {navItems.map((item) => (
        <button
          key={item.path}
          type="button"
          onClick={() => onNavigate(item.path)}
          className={`rounded-full px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] transition ${
            path === item.path
              ? "bg-emerald-400 text-slate-900"
              : "border border-slate-300 dark:border-slate-700 text-slate-700 dark:text-slate-200 hover:border-emerald-400/60"
          }`}
        >
          {item.label}
        </button>
      ))}
      <ThemeToggle
        theme={theme}
        onToggle={onToggleTheme}
        className="shrink-0"
      />
      <button
        type="button"
        onClick={onLogout}
        className="rounded-full border border-rose-500/60 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-rose-700 dark:text-rose-200 transition hover:border-rose-400 hover:text-rose-700 dark:hover:text-rose-100"
      >
        Logout
      </button>
    </nav>
  </header>
);
