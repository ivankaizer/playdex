import { useEffect, useState } from "react";

import { navItems, protectedPaths } from "./constants";
import { DashboardPage } from "./pages/DashboardPage";
import { GamePage } from "./pages/GamePage";
import { HomePage } from "./pages/HomePage";
import { MappingPage } from "./pages/MappingPage";
import { SessionsPage } from "./pages/SessionsPage";
import { SettingsPage } from "./pages/SettingsPage";
import { decodeGameHash, encodeGameId } from "./utils/gameHash";

export const App = () => {
  const [path, setPath] = useState(() => window.location.pathname);
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
    if (target === path) {
      return;
    }
    window.history.pushState({}, "", target);
    setPath(target);
    window.scrollTo({ top: 0, behavior: "smooth" });
  };

  const handleLogout = () => {
    localStorage.removeItem("playdex.token");
    localStorage.removeItem("playdex.refresh");
    localStorage.removeItem("playdex.email");
    window.location.assign("/");
  };

  const handleDashboardRedirect = () => {
    window.location.assign("/dashboard");
  };

  useEffect(() => {
    const handlePopState = () => setPath(window.location.pathname);
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
  const isProtectedPath = (target: string) =>
    protectedPaths.has(target) || target.startsWith("/games/");

  useEffect(() => {
    if (isLoggedIn || !isProtectedPath(path)) {
      return;
    }
    window.history.replaceState({}, "", "/");
    setPath("/");
  }, [isLoggedIn, path]);

  if (!isLoggedIn && isProtectedPath(path)) {
    return null;
  }

  if (path === "/dashboard") {
    return (
      <DashboardPage
        path={path}
        navItems={navItems}
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
        navItems={navItems}
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
        navItems={navItems}
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
        navItems={navItems}
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
        navItems={navItems}
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
