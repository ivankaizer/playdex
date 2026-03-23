import { useEffect, useState } from "react";

import { apiRequest } from "./api";
import { adminNavItem, navItems, protectedPaths } from "./constants";
import { AdminPage } from "./pages/AdminPage";
import { DashboardPage } from "./pages/DashboardPage";
import { GamePage } from "./pages/GamePage";
import { HomePage } from "./pages/HomePage";
import { MappingPage } from "./pages/MappingPage";
import { RecapPage } from "./pages/RecapPage";
import { SessionsPage } from "./pages/SessionsPage";
import { SettingsPage } from "./pages/SettingsPage";
import type { MeResponse } from "./types";
import { decodeGameHash, encodeGameId } from "./utils/gameHash";

export const App = () => {
  const [path, setPath] = useState(() => window.location.pathname);
  const [search, setSearch] = useState(() => window.location.search);
  const [isAdmin, setIsAdmin] = useState(
    () => localStorage.getItem("playdex.isAdmin") === "1"
  );
  const [isProfileResolved, setIsProfileResolved] = useState(
    () => !localStorage.getItem("playdex.token")
  );
  const [theme, setTheme] = useState<"light" | "dark">(() => {
    const stored = localStorage.getItem("playdex.theme");
    if (stored === "light" || stored === "dark") {
      return stored;
    }
    const prefersDark = window.matchMedia
      ? window.matchMedia("(prefers-color-scheme: dark)").matches
      : false;
    return prefersDark ? "dark" : "light";
  });

  const toggleTheme = () => {
    setTheme((current) => (current === "dark" ? "light" : "dark"));
  };

  const navigate = (target: string) => {
    const current = `${path}${search}`;
    if (target === current) {
      return;
    }
    window.history.pushState({}, "", target);
    setPath(window.location.pathname);
    setSearch(window.location.search);
    window.scrollTo({ top: 0, behavior: "smooth" });
  };

  const handleLogout = () => {
    localStorage.removeItem("playdex.token");
    localStorage.removeItem("playdex.refresh");
    localStorage.removeItem("playdex.email");
    localStorage.removeItem("playdex.isAdmin");
    window.location.assign("/");
  };

  const handleDashboardRedirect = () => {
    window.location.assign("/dashboard");
  };

  useEffect(() => {
    const handlePopState = () => {
      setPath(window.location.pathname);
      setSearch(window.location.search);
    };
    window.addEventListener("popstate", handlePopState);
    return () => window.removeEventListener("popstate", handlePopState);
  }, []);

  useEffect(() => {
    const root = document.documentElement;
    root.classList.toggle("dark", theme === "dark");
    root.dataset.theme = theme;
    localStorage.setItem("playdex.theme", theme);
  }, [theme]);

  const isLoggedIn = Boolean(localStorage.getItem("playdex.token"));
  const resolvedNavItems = isAdmin ? [...navItems, adminNavItem] : navItems;
  const isProtectedPath = (target: string) =>
    protectedPaths.has(target) || target.startsWith("/games/");

  useEffect(() => {
    if (!isLoggedIn) {
      setIsAdmin(false);
      setIsProfileResolved(true);
      return;
    }

    let cancelled = false;
    setIsProfileResolved(false);

    const loadProfile = async () => {
      try {
        const profile = await apiRequest<MeResponse>("/me");
        if (cancelled) {
          return;
        }
        setIsAdmin(profile.isAdmin);
        localStorage.setItem("playdex.isAdmin", profile.isAdmin ? "1" : "0");
      } catch {
        if (!cancelled) {
          setIsAdmin(false);
          localStorage.removeItem("playdex.isAdmin");
        }
      } finally {
        if (!cancelled) {
          setIsProfileResolved(true);
        }
      }
    };

    void loadProfile();
    return () => {
      cancelled = true;
    };
  }, [isLoggedIn]);

  useEffect(() => {
    if (isLoggedIn || !isProtectedPath(path)) {
      return;
    }
    window.history.replaceState({}, "", "/");
    setPath("/");
  }, [isLoggedIn, path]);

  useEffect(() => {
    if (path !== "/admin" || !isLoggedIn || !isProfileResolved || isAdmin) {
      return;
    }
    window.history.replaceState({}, "", "/dashboard");
    setPath("/dashboard");
    setSearch("");
  }, [isAdmin, isLoggedIn, isProfileResolved, path]);

  if (!isLoggedIn && isProtectedPath(path)) {
    return null;
  }

  if (path === "/dashboard") {
    return (
      <DashboardPage
        path={path}
        navItems={resolvedNavItems}
        onNavigate={navigate}
        onLogout={handleLogout}
        theme={theme}
        onToggleTheme={toggleTheme}
      />
    );
  }

  if (path === "/sessions") {
    return (
      <SessionsPage
        path={path}
        navItems={resolvedNavItems}
        onNavigate={navigate}
        onLogout={handleLogout}
        theme={theme}
        onToggleTheme={toggleTheme}
      />
    );
  }

  if (path === "/recap") {
    return (
      <RecapPage
        path={path}
        search={search}
        navItems={resolvedNavItems}
        onNavigate={navigate}
        onLogout={handleLogout}
        theme={theme}
        onToggleTheme={toggleTheme}
      />
    );
  }

  if (path === "/mapping") {
    return (
      <MappingPage
        path={path}
        navItems={resolvedNavItems}
        onNavigate={navigate}
        onLogout={handleLogout}
        theme={theme}
        onToggleTheme={toggleTheme}
      />
    );
  }

  if (path === "/settings") {
    return (
      <SettingsPage
        path={path}
        navItems={resolvedNavItems}
        onNavigate={navigate}
        onLogout={handleLogout}
        theme={theme}
        onToggleTheme={toggleTheme}
      />
    );
  }

  if (path === "/admin") {
    if (!isProfileResolved || !isAdmin) {
      return null;
    }
    return (
      <AdminPage
        path={path}
        navItems={resolvedNavItems}
        onNavigate={navigate}
        onLogout={handleLogout}
        theme={theme}
        onToggleTheme={toggleTheme}
      />
    );
  }

  if (path.startsWith("/games/")) {
    const raw = decodeURIComponent(path.replace("/games/", ""));
    const hash = raw.replace(/\/+$/, "").trim();
    const normalizedHash = /^\d+$/.test(hash) ? encodeGameId(hash) : hash;
    const gameId = decodeGameHash(normalizedHash) ? normalizedHash : null;
    return (
      <GamePage
        path={path}
        navItems={resolvedNavItems}
        gameId={gameId}
        onNavigate={navigate}
        onLogout={handleLogout}
        theme={theme}
        onToggleTheme={toggleTheme}
      />
    );
  }

  return (
    <HomePage
      isLoggedIn={isLoggedIn}
      onDashboardRedirect={handleDashboardRedirect}
      onLogout={handleLogout}
      theme={theme}
      onToggleTheme={toggleTheme}
    />
  );
};
