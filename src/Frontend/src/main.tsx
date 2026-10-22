import { StrictMode } from "react";
import { createRoot, type RootOptions } from "react-dom/client";
import { BrowserRouter } from "react-router";
import { App } from "./App";
import { AppErrorBoundary } from "./components/ErrorBoundary";
import { loadConfig } from "./lib/config";
import { ThemeProvider } from "./lib/theme";
import "./styles/app.css";

async function start() {
  const config = await loadConfig();
  let rootOptions: RootOptions | undefined;
  if (config.sentry?.dsn) {
    // Loaded only when configured, so deployments without Sentry never download it.
    const Sentry = await import("@sentry/react");
    Sentry.init({
      dsn: config.sentry.dsn,
      environment: config.sentry.environment,
      release: config.sentry.release ?? undefined,
      tracesSampleRate: config.sentry.tracesSampleRate,
      sendDefaultPii: false,
      integrations: [Sentry.browserTracingIntegration()],
      tracePropagationTargets: [config.apiBase],
      initialScope: { tags: { component: "web" } },
      ignoreErrors: ["AbortError"],
    });
    // Errors the error boundary catches never reach window.onerror, so they are reported from here.
    rootOptions = {
      onCaughtError: Sentry.reactErrorHandler((error, errorInfo) => console.error(error, errorInfo.componentStack)),
    };
  }

  createRoot(document.getElementById("app")!, rootOptions).render(
    <StrictMode>
      <BrowserRouter>
        <ThemeProvider>
          <AppErrorBoundary>
            <App />
          </AppErrorBoundary>
        </ThemeProvider>
      </BrowserRouter>
    </StrictMode>,
  );
}

void start();
