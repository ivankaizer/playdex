import { lazy, Suspense } from "react";
import { Navigate, Route, Routes, useLocation, useParams } from "react-router";
import { authSession } from "@/lib/auth";
import { normalizeGameHash } from "@/lib/navigation";
import { DashboardPage } from "@/pages/dashboard/DashboardPage";
import { GamePage } from "@/pages/games/GamePage";
import { HomePage } from "@/pages/home/HomePage";
import { PrivacyPage } from "@/pages/privacy/PrivacyPage";
import { RecapPage } from "@/pages/recap/RecapPage";
import { SessionCreatePage } from "@/pages/sessions/SessionCreatePage";
import { SessionsPage } from "@/pages/sessions/SessionsPage";
import { SettingsPage } from "@/pages/settings/SettingsPage";

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

export function App() {
  return (
    <Routes>
      <Route path="/" element={<HomePage />} />
      <Route path="/privacy" element={<PrivacyPage />} />
      <Route path="/dashboard" element={<RequireLogin><DashboardPage /></RequireLogin>} />
      <Route path="/sessions" element={<RequireLogin><SessionsPage /></RequireLogin>} />
      <Route path="/sessions/create" element={<RequireLogin><SessionCreatePage /></RequireLogin>} />
      <Route path="/recap" element={<RequireLogin><RecapPage /></RequireLogin>} />
      <Route path="/settings" element={<RequireLogin><SettingsPage /></RequireLogin>} />
      <Route path="/mapping" element={<RequireLogin><Navigate to="/settings#game-connections" replace /></RequireLogin>} />
      <Route path="/games/*" element={<RequireLogin><GameRoute /></RequireLogin>} />
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
  );
}
