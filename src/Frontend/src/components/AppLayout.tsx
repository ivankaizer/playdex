import { useCallback, type ReactNode } from "react";
import { useLocation } from "react-router";
import { apiRequest } from "@/lib/api";
import { logout, navItems, useAppNavigate } from "@/lib/navigation";
import { useTheme } from "@/lib/theme";
import type { GameSearchResponse } from "@/models/games";
import { AppFooter } from "./AppFooter";
import { AppHeader } from "./AppHeader";

interface AppLayoutProps {
  title: string;
  children?: ReactNode;
  overlay?: ReactNode;
}

/**
 * Page chrome: gradient background, header card, a vertical stack of page
 * sections (28px gap) and the dark footer.
 */
export function AppLayout({ title, children, overlay }: AppLayoutProps) {
  const { theme, toggleTheme } = useTheme();
  const location = useLocation();
  const navigate = useAppNavigate();

  const search = useCallback(
    (path: string, signal: AbortSignal) => apiRequest<GameSearchResponse>(path, { signal }),
    [],
  );

  return (
    <main className="pdx-page relative px-4 pb-10 pt-6 sm:px-8 sm:pt-12">
      <div className="relative mx-auto flex w-full max-w-[1180px] flex-col gap-7">
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
