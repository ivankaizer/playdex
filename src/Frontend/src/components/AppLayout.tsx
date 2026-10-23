import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { useLocation } from "react-router";
import { apiRequest } from "@/lib/api";
import { authSession, type MeResponse } from "@/lib/auth";
import { logout, navItems, useAppNavigate } from "@/lib/navigation";
import { useTheme } from "@/lib/theme";
import type { GameSearchResponse } from "@/models/games";
import { AppFooter } from "./AppFooter";
import { AppHeader } from "./AppHeader";

const AdminNavItem = { label: "Admin", path: "/admin" } as const;

/**
 * Whether the signed-in user is an administrator: the stored flag for an instant first render, refreshed
 * from /me in the background so a role change shows up on the next page load.
 */
function useIsAdmin(): boolean {
  const [isAdmin, setIsAdmin] = useState(() => authSession.isAdmin());
  useEffect(() => {
    const controller = new AbortController();
    void (async () => {
      try {
        const me = await apiRequest<MeResponse | undefined>("/me", { signal: controller.signal });
        if (controller.signal.aborted || !me) return;
        authSession.storeProfile(me);
        setIsAdmin(me.isAdmin === true);
      } catch {
        // Keep the stored flag; the admin console re-checks the role itself.
      }
    })();
    return () => controller.abort();
  }, []);
  return isAdmin;
}

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
  const isAdmin = useIsAdmin();
  const items = useMemo(() => (isAdmin ? [...navItems, AdminNavItem] : navItems), [isAdmin]);

  const search = useCallback((path: string, signal: AbortSignal) => apiRequest<GameSearchResponse>(path, { signal }), []);

  return (
    <main className="pdx-page relative px-4 pb-8 pt-6 sm:px-8 sm:pb-10 sm:pt-12">
      <div className="relative mx-auto flex w-full max-w-[1180px] flex-col gap-7">
        <AppHeader
          title={title}
          navItems={items}
          path={location.pathname}
          onNavigate={navigate}
          onLogout={logout}
          theme={theme}
          onToggleTheme={toggleTheme}
          search={search}
        />
        {children}
        <AppFooter items={items} />
      </div>
      {overlay}
    </main>
  );
}
