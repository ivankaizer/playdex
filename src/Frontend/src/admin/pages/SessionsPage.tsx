import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { Link, useSearchParams } from "react-router";
import { errorMessage } from "@/lib/api";
import { adminApi, isAbortError, type IntegrationSummary, type SessionPage } from "../api";
import { useNotifications } from "../components/Notifications";
import { Alert, Button, PageHeading, Pager, ProgressBar, rowClass, Select, Table, Td, TextInput, Th, Toolbar } from "../components/ui";
import { hasNextPage, integrationLabel, SessionStatusOptions } from "../logic";
import { formatLocal, parseTime } from "../time";

export function SessionsPage() {
  const [params] = useSearchParams();
  const scopeStatus = params.get("status");
  const scopeFrom = params.get("from");
  const scopeTo = params.get("to");
  const notify = useNotifications();

  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("");
  const [target, setTarget] = useState("");
  const [integrations, setIntegrations] = useState<IntegrationSummary[]>([]);
  const [result, setResult] = useState<SessionPage | null>(null);
  const [selected, setSelected] = useState<ReadonlySet<number>>(new Set());
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const inputs = useRef({ query, status, from: scopeFrom, to: scopeTo });
  inputs.current = { query, status, from: scopeFrom, to: scopeTo };
  const controllerRef = useRef<AbortController | null>(null);

  const load = useCallback(async (page: number, statusOverride?: string) => {
    if (page < 0) return;
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;
    setLoading(true);
    const current = inputs.current;
    const effectiveStatus = statusOverride ?? current.status;
    try {
      const next = await adminApi.sessions(
        {
          query: current.query,
          page,
          status: effectiveStatus === "" ? null : effectiveStatus,
          from: parseTime(current.from)?.toISOString() ?? null,
          to: parseTime(current.to)?.toISOString() ?? null,
        },
        { signal: controller.signal },
      );
      if (controller.signal.aborted) return;
      setResult(next);
      setSelected(new Set());
      setError(null);
    } catch (caught) {
      if (controller.signal.aborted || isAbortError(caught)) return;
      setError(errorMessage(caught));
    } finally {
      if (controllerRef.current === controller) setLoading(false);
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    adminApi
      .integrations({ signal: controller.signal })
      .then(setIntegrations)
      .catch((caught: unknown) => {
        if (!controller.signal.aborted && !isAbortError(caught)) setError(errorMessage(caught));
      });
    return () => controller.abort();
  }, []);

  // A new dashboard scope (status / started range) resets the status filter and reloads page one.
  useEffect(() => {
    const nextStatus = scopeStatus ?? "";
    setStatus(nextStatus);
    void load(0, nextStatus);
    return () => controllerRef.current?.abort();
  }, [scopeStatus, scopeFrom, scopeTo, load]);

  const search = (event: FormEvent) => {
    event.preventDefault();
    if (!loading) void load(0);
  };

  const toggle = (id: number) =>
    setSelected((current) => {
      const next = new Set(current);
      if (!next.delete(id)) next.add(id);
      return next;
    });

  const transfer = async () => {
    if (loading || target === "" || selected.size === 0) return;
    if (!window.confirm(`Transfer ${selected.size} selected sessions?`)) return;
    setLoading(true);
    let reload = false;
    try {
      const response = await adminApi.transferSessions([...selected], Number(target));
      notify.success("Sessions transferred", `Transferred ${response.transferred} sessions.`);
      reload = true;
    } catch (caught) {
      const message = errorMessage(caught);
      setError(message);
      notify.error("Transfer failed", message);
    } finally {
      setLoading(false);
    }
    if (reload) await load(result?.page ?? 0);
  };

  return (
    <>
      <PageHeading>Sessions</PageHeading>
      {scopeFrom !== null || scopeTo !== null ? (
        <p className="mb-2 text-sm text-slate-600 dark:text-slate-400">
          Started {formatLocal(scopeFrom)} – {formatLocal(scopeTo)} ·{" "}
          <Link className="text-emerald-600 hover:underline" to="/admin/sessions">
            All sessions
          </Link>
        </p>
      ) : null}
      <form onSubmit={search}>
        <Toolbar>
          <TextInput
            className="sm:w-72"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Email, game, account, or platform"
            aria-label="Search sessions"
          />
          <Select className="sm:w-40" value={status} onChange={(event) => setStatus(event.target.value)} aria-label="Status">
            <option value="">Any status</option>
            {SessionStatusOptions.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </Select>
          <Button type="submit" variant="primary" disabled={loading}>
            Search
          </Button>
        </Toolbar>
      </form>
      <Toolbar className="rounded-md bg-white p-2 dark:bg-slate-900">
        <Select className="sm:w-96" value={target} onChange={(event) => setTarget(event.target.value)} aria-label="Target integration">
          <option value="">Target integration</option>
          {integrations.map((integration) => (
            <option key={integration.id} value={integration.id}>
              {integrationLabel(integration)}
            </option>
          ))}
        </Select>
        <Button onClick={() => void transfer()} disabled={loading || selected.size === 0 || target === ""}>
          Transfer selected{selected.size > 0 ? ` (${selected.size})` : ""}
        </Button>
      </Toolbar>
      {error !== null ? (
        <Alert tone="danger">{error}</Alert>
      ) : loading || result === null ? (
        <ProgressBar />
      ) : result.sessions.length === 0 ? (
        <Alert>No sessions found.</Alert>
      ) : (
        <>
          <Table>
            <thead>
              <tr>
                <Th className="w-16 text-center">Select</Th>
                <Th>Owner</Th>
                <Th>Game</Th>
                <Th>Platform</Th>
                <Th>Started</Th>
                <Th>State</Th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 dark:divide-slate-800">
              {result.sessions.map((session) => (
                <tr key={session.id} className={rowClass}>
                  <Td className="text-center">
                    <input
                      type="checkbox"
                      aria-label={`Select session ${session.id}`}
                      checked={selected.has(session.id)}
                      onChange={() => toggle(session.id)}
                    />
                  </Td>
                  <Td>{session.email}</Td>
                  <Td>{session.game}</Td>
                  <Td>
                    <div>{session.platform}</div>
                    {session.accountId ? <small className="text-slate-500 dark:text-slate-400">{session.accountId}</small> : null}
                  </Td>
                  <Td>{formatLocal(session.startedAt)}</Td>
                  <Td>{session.live ? "Live" : "Ended"}</Td>
                </tr>
              ))}
            </tbody>
          </Table>
          <Pager
            label={`Total: ${result.total} | Page: ${result.page + 1}`}
            page={result.page}
            hasNext={hasNextPage(result.page, result.pageSize, result.total)}
            disabled={loading}
            onPage={(page) => void load(page)}
          />
        </>
      )}
    </>
  );
}
