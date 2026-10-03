import { useCallback, type ReactNode } from "react";
import { useLocation } from "react-router";
import { apiRequest } from "@/lib/api";
import { logout, navItems, useAppNavigate } from "@/lib/navigation";
import { useTheme } from "@/lib/theme";
import type { GameSearchResponse } from "@/models/games";
import { AppFooter } from "./AppFooter";
import { AppHeader } from "./AppHeader";
import { Background } from "./Background";

interface AppLayoutProps {
  title: string;
  children?: ReactNode;
  overlay?: ReactNode;
}

export function AppLayout({ title, children, overlay }: AppLayoutProps) {
  const { theme, toggleTheme } = useTheme();
  const location = useLocation();
  const navigate = useAppNavigate();

  const search = useCallback(
    (path: string, signal: AbortSignal) => apiRequest<GameSearchResponse>(path, { signal }),
    [],
  );

  return (
    <main className="relative min-h-screen overflow-hidden bg-slate-200 dark:bg-slate-950 text-slate-900 dark:text-slate-100">
      <Background />
      <div className="relative mx-auto w-full max-w-6xl p-3 sm:px-6 sm:py-12">
        <AppHeader
          title={title}
          navItems={navItems}
          path={location.pathname}
          onNavigate={navigate}
          onLogout={logout}
          theme={theme}
          onToggleTheme={toggleTheme}
          search={search}
        />
        {children}
        <AppFooter />
      </div>
      {overlay}
    </main>
  );
}
