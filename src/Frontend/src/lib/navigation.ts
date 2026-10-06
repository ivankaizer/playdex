import { useCallback } from "react";
import { useNavigate } from "react-router";
import { authSession } from "./auth";
import { encodeGameId, decodeGameHash } from "./gameHash";

export const protectedPaths = new Set([
  "/dashboard",
  "/sessions",
  "/sessions/create",
  "/recap",
  "/mapping",
  "/settings",
]);

export const navItems: ReadonlyArray<{ label: string; path: string }> = [
  { label: "Dashboard", path: "/dashboard" },
  { label: "Sessions", path: "/sessions" },
  { label: "Recap", path: "/recap" },
  { label: "Settings", path: "/settings" },
];

export function normalizeGameHash(escapedRemainder: string): string | null {
  let decoded: string;
  try {
    decoded = decodeURIComponent(escapedRemainder);
  } catch {
    return null;
  }

  let hash = decoded.replace(/\/+$/, "").trim();
  if (/^[0-9]*$/.test(hash)) hash = encodeGameId(hash);
  return decodeGameHash(hash) !== null ? hash : null;
}

export function scrollToTop(): void {
  const reduceMotion =
    typeof window.matchMedia === "function" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  window.scrollTo({ top: 0, behavior: reduceMotion ? "auto" : "smooth" });
}

/**
 * Navigates within the app: a no-op when already on the target, otherwise pushes
 * the target and scrolls smoothly to the top.
 */
export function useAppNavigate(): (target: string) => void {
  const navigate = useNavigate();
  return useCallback(
    (target: string) => {
      const current = window.location.pathname + window.location.search;
      if (current === target) return;
      navigate(target);
      scrollToTop();
    },
    [navigate],
  );
}

/** Clears the stored session and reloads the app at the home page. */
export function logout(): void {
  authSession.logout();
  window.location.assign("/");
}
