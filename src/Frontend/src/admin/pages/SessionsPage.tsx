import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { Link, useSearchParams } from "react-router";
import { errorMessage } from "@/lib/api";
import { cx } from "@/lib/cx";
import { adminApi, isAbortError, type IntegrationSummary, type SessionPage } from "../api";
import { useNotifications } from "../components/Notifications";
import { Alert, Badge, Button, linkClass, Note, Pager, Panel, ProgressBar, rowClass, Select, Sub, Table, Td, TextInput, Th, Toolbar } from "../components/ui";
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

  const remove = async () => {
    if (loading || selected.size === 0) return;
    const count = selected.size;
    if (!window.confirm(`Delete ${count} selected ${count === 1 ? "session" : "sessions"}? This can't be undone.`)) return;
    setLoading(true);
    let reload = false;
    try {
      const response = await adminApi.deleteSessions([...selected]);
      notify.success("Sessions deleted", `Deleted ${response.deleted} ${response.deleted === 1 ? "session" : "sessions"}.`);
      reload = true;
    } catch (caught) {
      const message = errorMessage(caught);
      setError(message);
      notify.error("Delete failed", message);
    } finally {
      setLoading(false);
    }
    // The page may now be past the end: stay on it only if something is left there.
    if (reload) await load(result && result.sessions.length > count ? result.page : Math.max(0, (result?.page ?? 0) - 1));
  };

  return (
    <>
      <Panel label="Sessions" kicker="Operate" title="All sessions">
        {scopeFrom !== null || scopeTo !== null ? (
          <Note>
            Started {formatLocal(scopeFrom)} – {formatLocal(scopeTo)} ·{" "}
            <Link className={linkClass} to="/admin/sessions">
              All sessions
            </Link>
          </Note>
        ) : null}
        <form onSubmit={search}>
          <Toolbar>
            <TextInput
              className="sm:!w-72"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder="Email, game, account, or platform"
              aria-label="Search sessions"
            />
            <Select className="sm:!w-44" value={status} onChange={(event) => setStatus(event.target.value)} aria-label="Status">
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
        <div className="pdx-tile flex flex-col gap-3 !px-5 !py-4">
          <p className="pdx-label">With the selected sessions</p>
          <Toolbar>
            <Select
              className="sm:!w-96"
              value={target}
              onChange={(event) => setTarget(event.target.value)}
              aria-label="Target integration"
            >
              <option value="">Target integration</option>
              {integrations.map((integration) => (
                <option key={integration.id} value={integration.id}>
                  {integrationLabel(integration)}
                </option>
              ))}
            </Select>
            <Button
              variant="accent"
              onClick={() => void transfer()}
              disabled={loading || selected.size === 0 || target === ""}
            >
              Transfer selected{selected.size > 0 ? ` (${selected.size})` : ""}
            </Button>
            <Button variant="danger" onClick={() => void remove()} disabled={loading || selected.size === 0}>
              Delete selected{selected.size > 0 ? ` (${selected.size})` : ""}
            </Button>
          </Toolbar>
        </div>
        {error !== null ? (
          <Alert tone="danger">{error}</Alert>
        ) : loading || result === null ? (
          <ProgressBar />
        ) : result.sessions.length === 0 ? (
          <Alert>No sessions found.</Alert>
        ) : (
          <>
            <Table label="Sessions table">
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
              <tbody>
                {result.sessions.map((session) => (
                  <tr key={session.id} className={cx(rowClass, selected.has(session.id) && "bg-mint/60")}>
                    <Td className="text-center">
                      <input
                        type="checkbox"
                        className="pdx-hit h-4 w-4 cursor-pointer accent-brand"
                        aria-label={`Select session ${session.id}`}
                        checked={selected.has(session.id)}
                        onChange={() => toggle(session.id)}
                      />
                    </Td>
                    <Td className="break-all">{session.email}</Td>
                    <Td className="min-w-[160px] font-semibold text-ink">{session.game}</Td>
                    <Td>
                      <span className="whitespace-nowrap">{session.platform}</span>
                      {session.accountId ? <Sub>{session.accountId}</Sub> : null}
                    </Td>
                    <Td className="pdx-num whitespace-nowrap">{formatLocal(session.startedAt)}</Td>
                    <Td>
                      {session.live ? (
                        <Badge tone="success" live>
                          Live
                        </Badge>
                      ) : (
                        <Badge>Ended</Badge>
                      )}
                    </Td>
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
      </Panel>
    </>
  );
}
