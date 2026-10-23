import { Component, type ReactNode } from "react";
import { useLocation } from "react-router";

interface ErrorBoundaryProps {
  children: ReactNode;
  /** A new value clears a caught error and renders the children again (the page's path: navigating away recovers). */
  resetKey?: unknown;
}

interface ErrorBoundaryState {
  error: unknown;
}

/**
 * Catches a render error anywhere below it, so a bug or a page chunk that fails to load (a deploy replaced it) shows a
 * way out instead of a blank page. React reports the error (to Sentry when it is configured, see main.tsx).
 */
export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: unknown): ErrorBoundaryState {
    return { error: error ?? new Error("Unknown render error.") };
  }

  componentDidUpdate(previous: ErrorBoundaryProps): void {
    if (this.state.error !== null && previous.resetKey !== this.props.resetKey) this.setState({ error: null });
  }

  render(): ReactNode {
    return this.state.error === null ? this.props.children : <ErrorFallback />;
  }
}

/** The app's boundary: it resets when the path changes, so the header's links lead out of a broken page. */
export function AppErrorBoundary({ children }: { children: ReactNode }) {
  const location = useLocation();
  return <ErrorBoundary resetKey={location.pathname}>{children}</ErrorBoundary>;
}

function ErrorFallback() {
  return (
    <main className="pdx-page grid min-h-screen place-items-center px-4 py-10">
      <section role="alert" className="pdx-card flex w-full max-w-[520px] flex-col gap-4">
        <p className="pdx-kicker">Playdex</p>
        <h1 className="pdx-title">Something went wrong</h1>
        <p className="text-[15px] text-muted">
          This page ran into an error. Your play data is safe. Reloading usually fixes it; a new version of Playdex may
          have been released since this page was opened.
        </p>
        <div className="flex flex-wrap gap-3">
          <button type="button" onClick={() => window.location.reload()} className="pdx-btn-primary">
            Reload page
          </button>
          {/* A full page load, not the router: the app's state is what broke. */}
          <a href="/dashboard" className="pdx-btn-outline no-underline">
            Go to dashboard
          </a>
        </div>
      </section>
    </main>
  );
}
