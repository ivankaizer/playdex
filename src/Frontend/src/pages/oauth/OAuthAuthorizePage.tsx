import { useEffect, useMemo, useState, type FormEvent } from "react";
import { useLocation } from "react-router";
import { ThemeToggle } from "@/components/ThemeToggle";
import { apiRequest, errorMessage } from "@/lib/api";
import { authSession, type LoginResponse } from "@/lib/auth";
import { useTheme } from "@/lib/theme";

/** The authorization request as the client sent it (RFC 6749 section 4.1.1), passed on to the API unchanged. */
export interface OAuthAuthorizeRequest {
  responseType: string | null;
  clientId: string | null;
  redirectUri: string | null;
  codeChallenge: string | null;
  codeChallengeMethod: string | null;
  state: string | null;
  scope: string | null;
  resource: string | null;
}

interface OAuthConsent {
  clientId: string;
  clientName: string;
  clientUri: string | null;
  redirectHost: string;
  loopbackOnly: boolean;
  resourceName: string;
  scopes: { name: string; description: string }[];
}

interface OAuthAuthorizeCheck {
  consent: OAuthConsent | null;
  redirectUrl: string | null;
}

type View = { kind: "loading" } | { kind: "consent"; consent: OAuthConsent } | { kind: "error"; message: string } | { kind: "leaving" };

export function readAuthorizeRequest(search: string): OAuthAuthorizeRequest {
  const query = new URLSearchParams(search);
  return {
    responseType: query.get("response_type"),
    clientId: query.get("client_id"),
    redirectUri: query.get("redirect_uri"),
    codeChallenge: query.get("code_challenge"),
    codeChallengeMethod: query.get("code_challenge_method"),
    state: query.get("state"),
    scope: query.get("scope"),
    resource: query.get("resource"),
  };
}

/**
 * Where an OAuth client, such as an assistant connecting over MCP, sends the user to connect Playdex: sign in if
 * needed, see which app is asking, for what, and where it sends them back, then allow or cancel. Either way the browser returns to
 * the app; only an unknown app or redirect stays here with an error.
 */
export function OAuthAuthorizePage({ leave = (url: string) => window.location.assign(url) }: { leave?: (url: string) => void }) {
  const location = useLocation();
  const request = useMemo(() => readAuthorizeRequest(location.search), [location.search]);
  const { theme, toggleTheme } = useTheme();
  const [signedIn, setSignedIn] = useState(authSession.isLoggedIn());
  const [view, setView] = useState<View>({ kind: "loading" });
  const [busy, setBusy] = useState(false);

  const signOut = () => {
    authSession.logout();
    setSignedIn(false);
  };

  useEffect(() => {
    if (!signedIn) return;
    const controller = new AbortController();
    setView({ kind: "loading" });
    apiRequest<OAuthAuthorizeCheck>("/oauth/authorize/check", {
      method: "POST",
      body: request,
      signal: controller.signal,
      onAuthFailure: signOut,
    })
      .then((check) => {
        if (check.redirectUrl) {
          setView({ kind: "leaving" });
          leave(check.redirectUrl);
        } else if (check.consent) {
          setView({ kind: "consent", consent: check.consent });
        }
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) setView({ kind: "error", message: errorMessage(error) });
      });
    return () => controller.abort();
  }, [request, signedIn]);

  const decide = async (approve: boolean) => {
    setBusy(true);
    try {
      const result = await apiRequest<{ redirectUrl: string }>("/oauth/authorize", {
        method: "POST",
        body: { request, approve },
        onAuthFailure: signOut,
      });
      setView({ kind: "leaving" });
      leave(result.redirectUrl);
    } catch (error) {
      setView({ kind: "error", message: errorMessage(error) });
      setBusy(false);
    }
  };

  return (
    <main className="pdx-page relative px-4 pb-10 pt-6 sm:px-8 sm:pt-8">
      <div className="relative mx-auto flex w-full max-w-[520px] flex-col gap-7">
        <header className="flex items-center gap-4 rounded-full border border-[color:var(--card-border)] bg-surface/60 py-3 pl-6 pr-3">
          <div className="mr-auto flex items-center gap-2.5">
            <span className="h-2.5 w-2.5 rounded-full bg-bar shadow-[0_0_0_4px_rgba(47,194,142,.18)]"></span>
            <span className="text-xs font-semibold uppercase tracking-[0.32em] text-accent">Playdex</span>
          </div>
          <ThemeToggle theme={theme} onToggle={toggleTheme} />
        </header>

        <section className="flex flex-col gap-5 rounded-card border border-[color:var(--card-border)] bg-surface/85 px-5 py-7 shadow-pop min-[601px]:p-9">
          {!signedIn ? (
            <SignIn onSignedIn={() => setSignedIn(true)} />
          ) : view.kind === "loading" ? (
            <p className="text-sm text-muted" role="status">
              Checking the app…
            </p>
          ) : view.kind === "leaving" ? (
            <p className="text-sm text-muted" role="status">
              Returning to the app…
            </p>
          ) : view.kind === "error" ? (
            <div className="flex flex-col gap-3">
              <p className="pdx-kicker">Can’t connect</p>
              <h1 className="m-0 text-2xl font-semibold">This connection request doesn’t look right</h1>
              <p role="alert" className="text-sm text-danger">
                {view.message}
              </p>
              <p className="text-sm text-muted">Start connecting again from the app you were using.</p>
            </div>
          ) : (
            <Consent consent={view.consent} busy={busy} onDecide={(approve) => void decide(approve)} onSignOut={signOut} />
          )}
        </section>
      </div>
    </main>
  );
}

function Consent({
  consent,
  busy,
  onDecide,
  onSignOut,
}: {
  consent: OAuthConsent;
  busy: boolean;
  onDecide: (approve: boolean) => void;
  onSignOut: () => void;
}) {
  const email = authSession.getEmail();
  return (
    <>
      <div>
        <p className="pdx-kicker">Connect an app</p>
        <h1 className="mt-1 text-2xl font-semibold [overflow-wrap:anywhere]">{consent.clientName} wants to use your Playdex</h1>
      </div>
      <div className="flex flex-col gap-2 text-sm text-ink-2">
        <p className="m-0 font-medium text-ink">Through {consent.resourceName}, it will be able to:</p>
        <ul className="m-0 flex list-disc flex-col gap-1 pl-5">
          {consent.scopes.map((scope) => (
            <li key={scope.name}>{scope.description}</li>
          ))}
        </ul>
      </div>
      <dl className="m-0 grid gap-1 rounded-[18px] bg-tile px-4 py-3 text-[13px]">
        <dt className="text-muted">After you choose, you return to</dt>
        <dd className="m-0 font-semibold [overflow-wrap:anywhere]">{consent.redirectHost}</dd>
      </dl>
      {consent.loopbackOnly ? (
        <p role="note" className="m-0 rounded-[18px] border border-[color:var(--card-border)] px-4 py-3 text-[13px] text-ink-2">
          This app runs on your own computer. Continue only if you just started connecting it yourself.
        </p>
      ) : null}
      <div className="flex flex-col gap-3 min-[421px]:flex-row-reverse">
        <button type="button" disabled={busy} onClick={() => onDecide(true)} className="pdx-btn-primary !h-12 flex-1">
          Allow
        </button>
        <button type="button" disabled={busy} onClick={() => onDecide(false)} className="pdx-btn-outline !h-12 flex-1 !text-ink">
          Cancel
        </button>
      </div>
      <p className="m-0 text-[13px] text-muted">
        Signed in as <span className="break-all font-semibold text-accent">{email ?? "your account"}</span>.{" "}
        <button type="button" onClick={onSignOut} className="font-semibold text-accent underline underline-offset-2">
          Use another account
        </button>
      </p>
    </>
  );
}

function SignIn({ onSignedIn }: { onSignedIn: () => void }) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setLoading(true);
    setError("");
    try {
      const response = await apiRequest<LoginResponse>("/login", {
        method: "POST",
        body: { email, password },
        authenticated: false,
      });
      authSession.storeLogin(response);
      onSignedIn();
    } catch (failure) {
      setError(errorMessage(failure));
      setLoading(false);
    }
  };

  return (
    <form aria-label="Sign in" className="flex flex-col gap-5" onSubmit={(event) => void submit(event)}>
      <div>
        <p className="pdx-kicker">Connect an app</p>
        <h1 className="mt-1 text-2xl font-semibold">Sign in to Playdex to continue</h1>
      </div>
      <label className="flex flex-col gap-1.5 text-xs font-medium text-muted">
        Email
        <input
          type="email"
          autoComplete="email"
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          className="pdx-input !h-12 !px-5 !text-[15px]"
        />
      </label>
      <label className="flex flex-col gap-1.5 text-xs font-medium text-muted">
        Password
        <input
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          className="pdx-input !h-12 !px-5 !text-[15px]"
        />
      </label>
      <button type="submit" disabled={loading || !email || !password} className="pdx-btn-primary !h-12 w-full">
        {loading ? "Signing in..." : "Sign in"}
      </button>
      {error ? (
        <p role="alert" className="m-0 text-[13px] text-danger">
          {error}
        </p>
      ) : null}
    </form>
  );
}
