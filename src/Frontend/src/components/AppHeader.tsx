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
    <header className="pdx-card relative z-30 flex flex-wrap items-center gap-4 !rounded-[28px] !py-[18px] !pl-7 !pr-5">
      <div className="mr-auto min-w-0">
        <p className="text-[11px] font-semibold uppercase tracking-[0.32em] text-accent">Playdex</p>
        <h1 className="truncate text-2xl font-semibold tracking-[-0.01em]">{title}</h1>
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <nav data-header-nav aria-label="Main" className="flex flex-wrap gap-1.5">
          {navItems.map((item) => {
            const active = path === item.path || (item.path !== "/" && path.startsWith(`${item.path}/`));
            return (
              <button
                key={item.path}
                type="button"
                onClick={() => onNavigate(item.path)}
                aria-current={active ? "page" : undefined}
                className={
                  active
                    ? "rounded-full border border-pill bg-pill px-[18px] py-[8px] text-[13px] font-semibold tracking-[0.04em] text-pill-ink"
                    : "rounded-full border border-line px-[18px] py-[8px] text-[13px] font-medium tracking-[0.04em] text-ink-2 transition hover:bg-surface"
                }
              >
                {item.label}
              </button>
            );
          })}
        </nav>
        <div data-header-utilities className="flex items-center gap-3">
          <ThemeToggle theme={theme} onToggle={onToggleTheme} />
          <div data-header-search className="w-[190px] min-w-0 max-sm:w-[150px]">
            <GameSearchBox search={search} onNavigate={onNavigate} />
          </div>
          <button
            type="button"
            aria-label="Log out"
            title="Log out"
            onClick={onLogout}
            className="pdx-icon-btn !border-danger-line text-danger-icon hover:!bg-danger-tint"
          >
            <svg aria-hidden="true" viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="1.9">
              <path d="M10 17l5-5-5-5M15 12H3M21 19V5a2 2 0 0 0-2-2h-6" strokeLinecap="round" strokeLinejoin="round"></path>
            </svg>
          </button>
        </div>
      </div>
    </header>
  );
}
