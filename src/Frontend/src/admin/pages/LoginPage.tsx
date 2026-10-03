import { useState, type FormEvent } from "react";
import { useLocation, useNavigate } from "react-router";
import { adminLogin } from "../auth";
import { Alert, Button, Card, Field, TextInput } from "../components/ui";

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
    <div className="flex min-h-screen items-center justify-center bg-slate-100 px-4 dark:bg-slate-950">
      <Card className="w-full max-w-sm">
        <h1 className="mb-4 text-xl font-semibold text-slate-900 dark:text-white">Admin login</h1>
        {error ? <Alert tone="danger">{error}</Alert> : null}
        <form className="flex flex-col gap-4" onSubmit={submit}>
          <Field label="Email" htmlFor="admin-email">
            <TextInput
              id="admin-email"
              name="email"
              type="email"
              placeholder="Email"
              autoComplete="username"
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
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              required
            />
          </Field>
          <Button type="submit" variant="primary" disabled={submitting}>
            {submitting ? "Signing in…" : "Sign in"}
          </Button>
        </form>
      </Card>
    </div>
  );
}
