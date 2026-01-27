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
      />
    );
  }

  return (
    <HomePage
      isLoggedIn={isLoggedIn}
      onDashboardRedirect={handleDashboardRedirect}
      onLogout={handleLogout}
    />
  );
};
