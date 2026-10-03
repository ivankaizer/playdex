import * as Sentry from "@sentry/react";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router";
import { App } from "./App";
import { loadConfig } from "./lib/config";
import { ThemeProvider } from "./lib/theme";
import "./styles/app.css";

async function start() {
  const config = await loadConfig();
  if (config.sentry?.dsn) {
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
  }

  createRoot(document.getElementById("app")!).render(
    <StrictMode>
      <BrowserRouter>
        <ThemeProvider>
          <App />
        </ThemeProvider>
      </BrowserRouter>
    </StrictMode>,
  );
}

void start();
