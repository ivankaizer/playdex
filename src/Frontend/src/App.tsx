import { lazy, Suspense } from "react";
import { Navigate, Route, Routes, useLocation, useParams } from "react-router";
import { authSession } from "@/lib/auth";
import { normalizeGameHash } from "@/lib/navigation";
import { HomePage } from "@/pages/home/HomePage";

// Home stays in the entry chunk (first paint for signed-out visitors); every other page loads on demand.
const DashboardPage = lazy(() => import("@/pages/dashboard/DashboardPage").then((module) => ({ default: module.DashboardPage })));
const GamePage = lazy(() => import("@/pages/games/GamePage").then((module) => ({ default: module.GamePage })));
const PrivacyPage = lazy(() => import("@/pages/privacy/PrivacyPage").then((module) => ({ default: module.PrivacyPage })));
const RecapPage = lazy(() => import("@/pages/recap/RecapPage").then((module) => ({ default: module.RecapPage })));
const SessionCreatePage = lazy(() =>
  import("@/pages/sessions/SessionCreatePage").then((module) => ({ default: module.SessionCreatePage })),
);
const SessionsPage = lazy(() => import("@/pages/sessions/SessionsPage").then((module) => ({ default: module.SessionsPage })));
const SettingsPage = lazy(() => import("@/pages/settings/SettingsPage").then((module) => ({ default: module.SettingsPage })));
const AdminApp = lazy(() => import("@/admin/AdminApp"));

function RequireLogin({ children }: { children: React.ReactNode }) {
  if (!authSession.isLoggedIn()) return <Navigate to="/" replace />;
  return <>{children}</>;
}

function GameRoute() {
  const params = useParams();
  const location = useLocation();
  // Use the raw (still escaped) path so encoded slashes and invalid escapes behave like the guard expects.
  const remainder = location.pathname.slice("/games/".length) || (params["*"] ?? "");
  const hash = normalizeGameHash(remainder);
  return <GamePage key={hash ?? ""} hash={hash} />;
}

/** Blank page in the app's background while a lazily loaded page arrives, so navigation never flashes. */
function PageFallback() {
  return <main className="pdx-page" aria-busy="true"></main>;
}

export function App() {
  return (
    <Suspense fallback={<PageFallback />}>
      <Routes>
        <Route path="/" element={<HomePage />} />
        <Route path="/privacy" element={<PrivacyPage />} />
        <Route
          path="/dashboard"
          element={
            <RequireLogin>
              <DashboardPage />
            </RequireLogin>
          }
        />
        <Route
          path="/sessions"
          element={
            <RequireLogin>
              <SessionsPage />
            </RequireLogin>
          }
        />
        <Route
          path="/sessions/create"
          element={
            <RequireLogin>
              <SessionCreatePage />
            </RequireLogin>
          }
        />
        <Route
          path="/recap"
          element={
            <RequireLogin>
              <RecapPage />
            </RequireLogin>
          }
        />
        <Route
          path="/settings"
          element={
            <RequireLogin>
              <SettingsPage />
            </RequireLogin>
          }
        />
        <Route
          path="/mapping"
          element={
            <RequireLogin>
              <Navigate to="/settings#game-connections" replace />
            </RequireLogin>
          }
        />
        <Route
          path="/games/*"
          element={
            <RequireLogin>
              <GameRoute />
            </RequireLogin>
          }
        />
        <Route
          path="/admin/*"
          element={
            <Suspense fallback={null}>
              <AdminApp />
            </Suspense>
          }
        />
        <Route path="*" element={<HomePage />} />
      </Routes>
    </Suspense>
  );
}
