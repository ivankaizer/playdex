import { useState, type FormEvent } from "react";
import { useLocation, useNavigate } from "react-router";
import { adminLogin } from "../auth";
import { Alert, Button, Field, TextInput } from "../components/ui";

interface LoginLocationState {
  from?: string;
  reason?: string | null;
}

export function LoginPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const state = (location.state ?? {}) as LoginLocationState;
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(state.reason ?? null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (submitting) return;
    setSubmitting(true);
    setError(null);
    const result = await adminLogin(email, password);
    setSubmitting(false);
    if (!result.ok) {
      setError(result.error);
      setPassword("");
      return;
    }
    const target = state.from && state.from.startsWith("/admin") && state.from !== "/admin/login" ? state.from : "/admin";
    navigate(target, { replace: true });
  };

  return (
    <main className="pdx-page grid place-items-center px-4 py-10">
      <div className="pdx-card flex w-full max-w-[420px] flex-col gap-6">
        <div>
          <p className="pdx-kicker">Playdex · Admin</p>
          <h1 className="pdx-title">Admin login</h1>
          <p className="mt-2 text-sm text-muted">Sign in with an administrator account.</p>
        </div>
        {error ? <Alert tone="danger">{error}</Alert> : null}
        <form className="flex flex-col gap-4" onSubmit={submit}>
          <Field label="Email" htmlFor="admin-email">
            <TextInput
              id="admin-email"
              name="email"
              type="email"
              placeholder="Email"
              autoComplete="username"
              className="!h-12"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              required
            />
          </Field>
          <Field label="Password" htmlFor="admin-password">
            <TextInput
              id="admin-password"
              name="password"
              type="password"
              placeholder="Password"
              autoComplete="current-password"
              className="!h-12"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              required
            />
          </Field>
          <Button type="submit" variant="primary" className="mt-2 !h-12" disabled={submitting}>
            {submitting ? "Signing in…" : "Sign in"}
          </Button>
        </form>
      </div>
    </main>
  );
}
