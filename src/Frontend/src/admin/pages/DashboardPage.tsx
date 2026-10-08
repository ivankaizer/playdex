import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { Link } from "react-router";
import { AdminAccessRequired, adminApi, isAbortError, isForbidden, type DashboardSession, type DashboardSnapshot } from "../api";
import { Cover } from "@/components/ui";
import { platformColor } from "@/lib/platforms";
import { Alert, Badge, Button, linkClass, Pager, Panel, ProgressBar, rowClass, Select, Sub, Table, Td, TextInput, Th, Toolbar } from "../components/ui";
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
      className="pdx-tile group flex flex-col no-underline transition-colors hover:border-mint-hover focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-bar"
    >
      <span className="pdx-label">{label}</span>
      <strong className="pdx-num mt-2.5 text-[36px] font-semibold leading-tight tracking-[-0.02em] text-ink">{value}</strong>
      <span aria-hidden="true" className="mt-1 text-[13px] font-medium text-accent opacity-0 transition-opacity group-hover:opacity-100">
        Open →
      </span>
    </Link>
  );
}

/** Platform name with its brand-colored dot. */
function PlatformName({ keyName, name }: { keyName: string; name: string }) {
  return (
    <span className="inline-flex items-center gap-2 whitespace-nowrap">
      <span aria-hidden="true" className="h-2 w-2 shrink-0 rounded-full" style={{ backgroundColor: platformColor(keyName, name) }} />
      {name}
    </span>
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
      <Panel
        label="Overview"
        kicker="Overview"
        title="Today across all accounts"
        actions={
          <>
            <span className="pdx-num text-sm text-muted">
              {snapshot === null ? "Monitoring all accounts" : `Updated ${formatLocalSeconds(snapshot.updatedAt)} · every 30s`}
            </span>
            <Button onClick={() => void refresh()} disabled={loading}>
              Refresh
            </Button>
          </>
        }
      >
        {loading ? <ProgressBar /> : null}
        {error !== null ? (
          <div className="flex flex-col items-start gap-3">
            <Alert tone="danger">{error}</Alert>
            <Button onClick={() => void refresh()} disabled={loading}>
              Retry
            </Button>
          </div>
        ) : null}
        {snapshot !== null ? (
          <div className="grid gap-3.5 [grid-template-columns:repeat(auto-fit,minmax(min(200px,100%),1fr))]">
            <Counter to="/admin/sessions?status=live" value={snapshot.counts.liveSessions} label="Live sessions" />
            <Counter to="/admin/sessions?status=live" value={snapshot.counts.liveUsers} label="Users playing" />
            <Counter
              to={day ? sessionsTodayLink(day.start, day.end) : "/admin/sessions"}
              value={snapshot.counts.sessionsToday}
              label="Sessions today"
            />
            <Counter to={aiErrorsLink(snapshot.updatedAt)} value={snapshot.counts.aiErrors} label="AI errors · 24h" />
          </div>
        ) : null}
      </Panel>
      {snapshot !== null && live !== null ? (
        <>
          <Panel
            label="Live sessions"
            kicker={
              <span className="inline-flex items-center gap-2">
                <span aria-hidden="true" className="pdx-live-dot" />
                Live now
              </span>
            }
            title="Live sessions"
            actions={<span className="text-sm text-muted">Longest running first</span>}
          >
            <form onSubmit={applyFilters}>
              <Toolbar>
                <TextInput
                  className="sm:!w-72"
                  value={query}
                  onChange={(event) => setQuery(event.target.value)}
                  placeholder="Search user, game or account"
                  aria-label="Search live sessions"
                />
                <Select
                  className="sm:!w-56"
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
                <Table label="Live sessions table">
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
                  <tbody>
                    {live.rows.map((session) => (
                      <tr key={session.id} className={rowClass}>
                        <Td className="break-all">{session.email}</Td>
                        <Td>
                          <div className="flex min-w-[180px] items-center gap-3">
                            <Cover
                              title={session.game}
                              src={session.capsuleUrl && session.capsuleUrl.trim() !== "" ? session.capsuleUrl : null}
                              className="h-9 w-14 rounded-[10px]"
                              initialClassName="text-xs"
                            />
                            <span className="font-semibold text-ink">{session.game}</span>
                          </div>
                        </Td>
                        <Td>
                          <PlatformName keyName={session.platformKey} name={session.platform} />
                          <Sub>{session.accountId ?? "—"}</Sub>
                        </Td>
                        <Td>{sourceLabel(session.source)}</Td>
                        <Td className="pdx-num whitespace-nowrap font-semibold text-accent">{duration(session)}</Td>
                        <Td className="pdx-num whitespace-nowrap">{formatLocal(session.startedAt)}</Td>
                        <Td className="pdx-num whitespace-nowrap">
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
          </Panel>

          <div className="grid gap-7 [grid-template-columns:repeat(auto-fit,minmax(min(420px,100%),1fr))]">
            <Panel
              label="Recent ended sessions"
              kicker="History"
              title="Recently ended"
              actions={
                <Link className={linkClass} to="/admin/sessions?status=ended">
                  View all
                </Link>
              }
            >
              {snapshot.endedSessions.length === 0 ? (
                <Alert>No ended sessions yet.</Alert>
              ) : (
                <ul className="-mx-3 flex flex-col">
                  {snapshot.endedSessions.map((session) => (
                    <li key={session.id} className="pdx-row flex items-center gap-3 px-3 py-2.5 text-sm">
                      <Cover
                        title={session.game}
                        src={session.capsuleUrl && session.capsuleUrl.trim() !== "" ? session.capsuleUrl : null}
                        className="h-10 w-10 rounded-[12px]"
                        initialClassName="text-sm"
                      />
                      <div className="flex min-w-0 flex-1 flex-col">
                        <strong className="truncate font-semibold">{session.game}</strong>
                        <small className="truncate text-xs text-muted">
                          {session.email} · {session.platform}
                        </small>
                      </div>
                      <div className="flex shrink-0 flex-col items-end">
                        <strong className="pdx-num font-semibold">{duration(session)}</strong>
                        <small className="pdx-num text-xs text-muted">{formatLocal(session.endedAt)}</small>
                      </div>
                    </li>
                  ))}
                </ul>
              )}
            </Panel>
            <Panel
              label="Recent AI errors"
              kicker="AI generations"
              title="Recent AI errors"
              actions={
                <Link className={linkClass} to="/admin/ai-generations?status=errors">
                  View all
                </Link>
              }
            >
              {snapshot.failedGenerations.length === 0 ? (
                <Alert>No failed AI generations.</Alert>
              ) : (
                <ul className="-mx-3 flex flex-col">
                  {snapshot.failedGenerations.map((generation) => (
                    <li key={generation.id}>
                      <Link
                        to={generationLink(generation.id)}
                        className="pdx-row flex items-center justify-between gap-3 px-3 py-2.5 text-sm no-underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-bar"
                      >
                        <div className="flex min-w-0 flex-col">
                          <strong className="truncate font-semibold text-ink">{generation.email}</strong>
                          <small className="truncate text-xs text-muted">{generation.model}</small>
                        </div>
                        <div className="flex shrink-0 flex-col items-end gap-1">
                          <Badge tone="danger">{generation.status}</Badge>
                          <small className="pdx-num text-xs text-muted">{formatLocal(generation.createdAt)}</small>
                        </div>
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
            </Panel>
          </div>
        </>
      ) : null}
    </>
  );
}
