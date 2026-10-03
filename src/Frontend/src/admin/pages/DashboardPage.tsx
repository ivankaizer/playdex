import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { Link } from "react-router";
import { AdminAccessRequired, adminApi, isAbortError, isForbidden, type DashboardSession, type DashboardSnapshot } from "../api";
import { Alert, Button, Card, PageHeading, Pager, ProgressBar, rowClass, SectionHeading, Select, Table, Td, TextInput, Th, Toolbar } from "../components/ui";
import { aiErrorsLink, DashboardRefreshMs, generationLink, pageOf, sessionsTodayLink, sourceLabel } from "../logic";
import { formatElapsed, formatLocal, formatLocalSeconds, localDayRange } from "../time";

const LivePageSize = 20;

interface AppliedFilters {
  query: string;
  platform: string | null;
}

function Counter({ to, value, label }: { to: string; value: number; label: string }) {
  return (
    <Link
      to={to}
      className="flex flex-col rounded-lg border border-slate-200 bg-white p-4 shadow-sm hover:border-emerald-400 dark:border-slate-700 dark:bg-slate-900"
    >
      <strong className="text-3xl font-semibold text-slate-900 dark:text-white">{value}</strong>
      <span className="text-sm text-slate-500 dark:text-slate-400">{label}</span>
    </Link>
  );
}

export function DashboardPage() {
  const [snapshot, setSnapshot] = useState<DashboardSnapshot | null>(null);
  const [day, setDay] = useState<{ start: string; end: string } | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [platform, setPlatform] = useState("");
  const [applied, setApplied] = useState<AppliedFilters>({ query: "", platform: null });
  const [livePage, setLivePage] = useState(0);

  const loadingRef = useRef(false);
  const appliedRef = useRef(applied);
  const controllerRef = useRef<AbortController | null>(null);

  const refresh = useCallback(async () => {
    // A tick that lands while a read is in flight is skipped, never queued.
    if (loadingRef.current) return;
    loadingRef.current = true;
    setLoading(true);
    const controller = new AbortController();
    controllerRef.current = controller;
    const range = localDayRange(new Date());
    const filters = appliedRef.current;
    try {
      const result = await adminApi.dashboard(
        {
          query: filters.query,
          platform: filters.platform,
          dayStart: range.start.toISOString(),
          dayEnd: range.end.toISOString(),
        },
        { signal: controller.signal },
      );
      if (controller.signal.aborted) return;
      setSnapshot(result);
      setDay({ start: range.start.toISOString(), end: range.end.toISOString() });
      setError(null);
    } catch (caught) {
      if (controller.signal.aborted || isAbortError(caught)) return;
      if (isForbidden(caught)) {
        setSnapshot(null);
        setError(AdminAccessRequired);
      } else {
        setError("Could not refresh the dashboard. Previously loaded data may be out of date.");
      }
    } finally {
      if (controllerRef.current === controller) {
        loadingRef.current = false;
        if (!controller.signal.aborted) setLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    void refresh();
    const timer = window.setInterval(() => void refresh(), DashboardRefreshMs);
    return () => {
      window.clearInterval(timer);
      controllerRef.current?.abort();
      loadingRef.current = false;
    };
  }, [refresh]);

  const applyFilters = (event: FormEvent) => {
    event.preventDefault();
    if (loadingRef.current) return;
    const next = { query: query.trim(), platform: platform === "" ? null : platform };
    appliedRef.current = next;
    setApplied(next);
    setLivePage(0);
    void refresh();
  };

  const duration = (session: DashboardSession) => formatElapsed(session.startedAt, session.endedAt, snapshot!.updatedAt);
  const live = snapshot ? pageOf(snapshot.liveSessions, livePage, LivePageSize) : null;

  return (
    <>
      <PageHeading
        actions={
          <div className="flex items-center gap-3 text-sm text-slate-500 dark:text-slate-400">
            <span>
              {snapshot === null ? "Monitoring all accounts" : `Updated ${formatLocalSeconds(snapshot.updatedAt)} · every 30s`}
            </span>
            <Button onClick={() => void refresh()} disabled={loading}>
              Refresh
            </Button>
          </div>
        }
      >
        Dashboard
      </PageHeading>
      {loading ? <ProgressBar /> : null}
      {error !== null ? (
        <>
          <Alert tone="danger">{error}</Alert>
          <Button onClick={() => void refresh()} disabled={loading}>
            Retry
          </Button>
        </>
      ) : null}
      {snapshot !== null && live !== null ? (
        <>
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            <Counter to="/admin/sessions?status=live" value={snapshot.counts.liveSessions} label="Live sessions" />
            <Counter to="/admin/sessions?status=live" value={snapshot.counts.liveUsers} label="Users playing" />
            <Counter
              to={day ? sessionsTodayLink(day.start, day.end) : "/admin/sessions"}
              value={snapshot.counts.sessionsToday}
              label="Sessions today"
            />
            <Counter to={aiErrorsLink(snapshot.updatedAt)} value={snapshot.counts.aiErrors} label="AI errors · 24h" />
          </div>

          <section aria-label="Live sessions">
            <SectionHeading aside="Longest running first">Live sessions</SectionHeading>
            <form onSubmit={applyFilters}>
              <Toolbar>
                <TextInput
                  className="sm:w-72"
                  value={query}
                  onChange={(event) => setQuery(event.target.value)}
                  placeholder="Search user, game or account"
                  aria-label="Search live sessions"
                />
                <Select
                  className="sm:w-56"
                  value={platform}
                  onChange={(event) => setPlatform(event.target.value)}
                  aria-label="Live platform"
                >
                  <option value="">All platforms</option>
                  {snapshot.platforms.map((item) => (
                    <option key={item.key} value={item.key}>
                      {item.name}
                    </option>
                  ))}
                </Select>
                <Button type="submit" variant="primary" disabled={loading}>
                  Search
                </Button>
              </Toolbar>
            </form>
            {snapshot.liveSessions.length === 0 ? (
              <Alert>
                {applied.query.length === 0 && applied.platform === null
                  ? "No live sessions right now."
                  : "No live sessions match these filters."}
              </Alert>
            ) : (
              <>
                <Table>
                  <thead>
                    <tr>
                      <Th>User</Th>
                      <Th>Game</Th>
                      <Th>Platform</Th>
                      <Th>Source</Th>
                      <Th>Elapsed</Th>
                      <Th>Started</Th>
                      <Th>Last poll</Th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100 dark:divide-slate-800">
                    {live.rows.map((session) => (
                      <tr key={session.id} className={rowClass}>
                        <Td>{session.email}</Td>
                        <Td>
                          <div className="flex items-center gap-2">
                            {session.capsuleUrl && session.capsuleUrl.trim() !== "" ? (
                              <img src={session.capsuleUrl} alt="" loading="lazy" className="h-8 w-14 rounded object-cover" />
                            ) : (
                              <span className="flex h-8 w-14 items-center justify-center rounded bg-slate-200 text-xs text-slate-500 dark:bg-slate-700 dark:text-slate-400">
                                ?
                              </span>
                            )}
                            <span>{session.game}</span>
                          </div>
                        </Td>
                        <Td>
                          <div>{session.platform}</div>
                          <small className="text-slate-500 dark:text-slate-400">{session.accountId ?? "—"}</small>
                        </Td>
                        <Td>{sourceLabel(session.source)}</Td>
                        <Td>{duration(session)}</Td>
                        <Td>{formatLocal(session.startedAt)}</Td>
                        <Td>
                          {session.source !== "polling"
                            ? "Not polled"
                            : session.lastPolledAt === null
                              ? "No poll recorded"
                              : formatLocal(session.lastPolledAt)}
                        </Td>
                      </tr>
                    ))}
                  </tbody>
                </Table>
                {live.pages > 1 ? (
                  <Pager
                    label={`Page ${live.page + 1} of ${live.pages}`}
                    page={live.page}
                    hasNext={live.page + 1 < live.pages}
                    onPage={setLivePage}
                  />
                ) : null}
              </>
            )}
          </section>

          <div className="grid gap-6 lg:grid-cols-2">
            <section aria-label="Recent ended sessions">
              <SectionHeading aside={<Link className="text-emerald-600 hover:underline" to="/admin/sessions?status=ended">View all</Link>}>
                Recently ended
              </SectionHeading>
              {snapshot.endedSessions.length === 0 ? (
                <Alert>No ended sessions yet.</Alert>
              ) : (
                <Card className="divide-y divide-slate-100 p-0 dark:divide-slate-800">
                  {snapshot.endedSessions.map((session) => (
                    <div key={session.id} className="flex items-start justify-between gap-3 px-4 py-2 text-sm">
                      <div className="flex min-w-0 flex-col">
                        <strong className="truncate">{session.game}</strong>
                        <small className="truncate text-slate-500 dark:text-slate-400">
                          {session.email} · {session.platform}
                        </small>
                      </div>
                      <div className="flex shrink-0 flex-col items-end">
                        <strong>{duration(session)}</strong>
                        <small className="text-slate-500 dark:text-slate-400">{formatLocal(session.endedAt)}</small>
                      </div>
                    </div>
                  ))}
                </Card>
              )}
            </section>
            <section aria-label="Recent AI errors">
              <SectionHeading
                aside={<Link className="text-emerald-600 hover:underline" to="/admin/ai-generations?status=errors">View all</Link>}
              >
                Recent AI errors
              </SectionHeading>
              {snapshot.failedGenerations.length === 0 ? (
                <Alert>No failed AI generations.</Alert>
              ) : (
                <Card className="divide-y divide-slate-100 p-0 dark:divide-slate-800">
                  {snapshot.failedGenerations.map((generation) => (
                    <Link
                      key={generation.id}
                      to={generationLink(generation.id)}
                      className="flex items-start justify-between gap-3 px-4 py-2 text-sm hover:bg-slate-50 dark:hover:bg-slate-800"
                    >
                      <div className="flex min-w-0 flex-col">
                        <strong className="truncate">{generation.email}</strong>
                        <small className="truncate text-slate-500 dark:text-slate-400">{generation.model}</small>
                      </div>
                      <div className="flex shrink-0 flex-col items-end">
                        <span className="rounded bg-rose-100 px-1.5 text-xs font-medium text-rose-700 dark:bg-rose-950 dark:text-rose-300">
                          {generation.status}
                        </span>
                        <small className="text-slate-500 dark:text-slate-400">{formatLocal(generation.createdAt)}</small>
                      </div>
                    </Link>
                  ))}
                </Card>
              )}
            </section>
          </div>
        </>
      ) : null}
    </>
  );
}
