import type { NavItem } from "../types";

type AppHeaderProps = {
  title: string;
  navItems: NavItem[];
  path: string;
  onNavigate: (path: string) => void;
  onLogout: () => void;
};

export const AppHeader = ({
  title,
  navItems,
  path,
  onNavigate,
  onLogout,
}: AppHeaderProps) => (
  <header className="flex flex-wrap items-center justify-between gap-4 rounded-3xl border border-slate-800 bg-slate-900/70 px-6 py-4 md:flex-nowrap">
    <div className="min-w-0 flex-1">
      <p className="text-xs font-semibold uppercase tracking-[0.5em] text-emerald-300">
        Playdex
      </p>
      <h1 className="truncate text-2xl font-semibold text-white">{title}</h1>
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
              : "border border-slate-700 text-slate-200 hover:border-emerald-400/60"
          }`}
        >
          {item.label}
        </button>
      ))}
      <button
        type="button"
        onClick={onLogout}
        className="rounded-full border border-rose-500/60 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-rose-200 transition hover:border-rose-400 hover:text-rose-100"
      >
        Logout
      </button>
    </nav>
  </header>
);
