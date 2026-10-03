import { useEffect, useState, type ReactNode } from "react";
import { Navigate, Route, Routes, useLocation } from "react-router";
import { errorMessage, isAbortError } from "@/lib/api";
import { checkAdminSession, NotAdministratorMessage } from "./auth";
import { AdminLayout } from "./AdminLayout";
import { AdminUserContext } from "./adminUser";
import { NotificationProvider } from "./components/Notifications";
import { Alert, Button, ProgressBar } from "./components/ui";
import { AiGenerationsPage } from "./pages/AiGenerationsPage";
import { DashboardPage } from "./pages/DashboardPage";
import { GamesPage } from "./pages/GamesPage";
import { IntegrationsPage } from "./pages/IntegrationsPage";
import { LoginPage } from "./pages/LoginPage";
import { SessionsPage } from "./pages/SessionsPage";

type GateState =
  | { status: "checking" }
  | { status: "admin"; email: string }
  | { status: "redirect"; reason: string | null }
  | { status: "error"; message: string };

/** Lets only current administrators through; everyone else lands on /admin/login. */
function RequireAdmin({ children }: { children: ReactNode }) {
  const location = useLocation();
  const [state, setState] = useState<GateState>({ status: "checking" });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setState({ status: "checking" });
    checkAdminSession(controller.signal)
      .then((result) => {
        if (controller.signal.aborted) return;
        if (result.status === "admin") setState({ status: "admin", email: result.email });
        else setState({ status: "redirect", reason: result.status === "forbidden" ? NotAdministratorMessage : null });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted || isAbortError(error)) return;
        setState({ status: "error", message: errorMessage(error) });
      });
    return () => controller.abort();
  }, [attempt]);

  if (state.status === "checking") {
    return (
      <div className="min-h-screen bg-slate-100 p-8 dark:bg-slate-950">
        <ProgressBar />
      </div>
    );
  }
  if (state.status === "redirect") {
    return (
      <Navigate
        to="/admin/login"
        replace
        state={{ from: location.pathname + location.search, reason: state.reason }}
      />
    );
  }
  if (state.status === "error") {
    return (
      <div className="min-h-screen bg-slate-100 p-8 dark:bg-slate-950">
        <Alert tone="danger">Could not verify your administrator session: {state.message}</Alert>
        <Button onClick={() => setAttempt((value) => value + 1)}>Retry</Button>
      </div>
    );
  }
  return <AdminUserContext.Provider value={state.email}>{children}</AdminUserContext.Provider>;
}

export default function AdminApp() {
  useEffect(() => {
    const previous = document.title;
    document.title = "Playdex Admin";
    return () => {
      document.title = previous;
    };
  }, []);

  return (
    <NotificationProvider>
      <Routes>
        <Route path="login" element={<LoginPage />} />
        <Route
          element={
            <RequireAdmin>
              <AdminLayout />
            </RequireAdmin>
          }
        >
          <Route index element={<DashboardPage />} />
          <Route path="sessions" element={<SessionsPage />} />
          <Route path="games" element={<GamesPage />} />
          <Route path="integrations" element={<IntegrationsPage />} />
          <Route path="ai-generations" element={<AiGenerationsPage />} />
          <Route path="ai-generations/:id" element={<AiGenerationsPage />} />
          <Route path="*" element={<Navigate to="/admin" replace />} />
        </Route>
      </Routes>
    </NotificationProvider>
  );
}
