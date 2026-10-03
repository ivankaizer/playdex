import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { Link, useParams, useSearchParams } from "react-router";
import {
  AdminAccessRequired,
  adminApi,
  isAbortError,
  isForbidden,
  isNotFound,
  type GenerationDetail,
  type GenerationPage,
} from "../api";
import { GenerationDetails } from "../components/GenerationDetails";
import { Alert, Button, PageHeading, Pager, ProgressBar, rowClass, Select, Table, Td, TextInput, Th, Toolbar } from "../components/ui";
import { GenerationStatuses, hasNextPage } from "../logic";
import { formatLocal, parseTime } from "../time";

export function AiGenerationsPage() {
  const [params] = useSearchParams();
  const routeParams = useParams();
  const scopeId = routeParams.id ?? params.get("id");
  const scopeStatus = params.get("status");
  const scopeSince = params.get("since");
  const scopeUntil = params.get("until");

  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("");
  const [result, setResult] = useState<GenerationPage | null>(null);
  const [detail, setDetail] = useState<GenerationDetail | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const inputs = useRef({ query, status, since: scopeSince, until: scopeUntil });
  inputs.current = { query, status, since: scopeSince, until: scopeUntil };
  const controllerRef = useRef<AbortController | null>(null);

  const begin = () => {
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;
    setBusy(true);
    return controller;
  };
  const end = (controller: AbortController) => {
    if (controllerRef.current === controller) setBusy(false);
  };

  const load = useCallback(async (page: number, statusOverride?: string): Promise<boolean> => {
    if (page < 0) return false;
    const controller = begin();
    const current = inputs.current;
    const effectiveStatus = statusOverride ?? current.status;
    try {
      const next = await adminApi.generations(
        {
          query: current.query,
          status: effectiveStatus === "" ? null : effectiveStatus,
          page,
          since: parseTime(current.since)?.toISOString() ?? null,
          until: parseTime(current.until)?.toISOString() ?? null,
        },
        { signal: controller.signal },
      );
      if (controller.signal.aborted) return false;
      setResult(next);
      setDetail(null);
      setError(null);
      return true;
    } catch (caught) {
      if (controller.signal.aborted || isAbortError(caught)) return false;
      if (isForbidden(caught)) {
        setDetail(null);
        setResult(null);
        setError(AdminAccessRequired);
      } else {
        setError("Could not load generation history. Try searching again.");
      }
      return false;
    } finally {
      end(controller);
    }
  }, []);

  const inspect = useCallback(async (id: string) => {
    const controller = begin();
    setDetail(null);
    try {
      const next = await adminApi.generation(id, { signal: controller.signal });
      if (controller.signal.aborted) return;
      setDetail(next);
      setError(null);
      window.scrollTo({ top: 0, behavior: "smooth" });
    } catch (caught) {
      if (controller.signal.aborted || isAbortError(caught)) return;
      if (isNotFound(caught)) setError("This generation has been deleted or expired.");
      else if (isForbidden(caught)) {
        setResult(null);
        setError(AdminAccessRequired);
      } else setError("Could not load this generation. Try again.");
    } finally {
      end(controller);
    }
  }, []);

  // A new scope (dashboard link, outcome or time window) resets the outcome filter, reloads page one
  // and then opens the requested generation, if any.
  useEffect(() => {
    const nextStatus = scopeStatus ?? "";
    setStatus(nextStatus);
    let cancelled = false;
    void (async () => {
      const loaded = await load(0, nextStatus);
      if (!cancelled && loaded && scopeId) await inspect(scopeId);
    })();
    return () => {
      cancelled = true;
      controllerRef.current?.abort();
    };
  }, [scopeId, scopeStatus, scopeSince, scopeUntil, load, inspect]);

  const search = (event: FormEvent) => {
    event.preventDefault();
    if (!busy) void load(0);
  };

  const remove = async () => {
    if (busy || detail === null) return;
    if (!window.confirm("Delete this generation and its retained input, screenshot, and results?")) return;
    const controller = begin();
    let reload = false;
    try {
      await adminApi.deleteGeneration(detail.id);
      if (controller.signal.aborted) return;
      setDetail(null);
      reload = true;
    } catch (caught) {
      if (controller.signal.aborted) return;
      if (isForbidden(caught)) {
        setDetail(null);
        setResult(null);
        setError(AdminAccessRequired);
      } else setError("Could not delete this generation. Try again.");
    } finally {
      end(controller);
    }
    if (reload) await load(0);
  };

  return (
    <>
      <PageHeading>AI generations</PageHeading>
      <p className="mb-2 text-sm text-slate-600 dark:text-slate-400">
        Compare submitted evidence with generated sessions, including failed attempts and automatic retries.
      </p>
      {scopeSince !== null || scopeUntil !== null ? (
        <p className="mb-2 text-sm text-slate-600 dark:text-slate-400">
          Submitted {formatLocal(scopeSince)} – {formatLocal(scopeUntil)} ·{" "}
          <Link className="text-emerald-600 hover:underline" to="/admin/ai-generations?status=errors">
            Clear time filter
          </Link>
        </p>
      ) : null}
      <form onSubmit={search}>
        <Toolbar>
          <TextInput
            className="sm:w-72"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Search email or input text"
            aria-label="Search generation history"
          />
          <Select className="sm:w-44" value={status} onChange={(event) => setStatus(event.target.value)} aria-label="Generation outcome">
            <option value="">All outcomes</option>
            {GenerationStatuses.map((value) => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </Select>
          <Button type="submit" variant="primary" disabled={busy}>
            Search
          </Button>
        </Toolbar>
      </form>
      {error !== null ? <Alert tone="danger">{error}</Alert> : null}
      {busy ? <ProgressBar /> : null}
      {detail !== null ? (
        <div className="mb-6">
          <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
            <h2 className="text-lg font-semibold">Generation details</h2>
            <div className="flex gap-2">
              <Button onClick={() => setDetail(null)} disabled={busy}>
                Close details
              </Button>
              <Button variant="danger" onClick={() => void remove()} disabled={busy}>
                Delete generation
              </Button>
            </div>
          </div>
          <GenerationDetails detail={detail} />
        </div>
      ) : null}
      {result !== null && !busy ? (
        <>
          {result.items.length === 0 ? (
            <Alert>No generations found. History is collected for new submissions; older inputs were not retained.</Alert>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>User</Th>
                  <Th>Submitted</Th>
                  <Th>Outcome</Th>
                  <Th>Model</Th>
                  <Th>Input</Th>
                  <Th className="w-28">Review</Th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 dark:divide-slate-800">
                {result.items.map((item) => (
                  <tr key={item.id} className={rowClass}>
                    <Td>{item.email}</Td>
                    <Td className="whitespace-nowrap">{formatLocal(item.createdAt)}</Td>
                    <Td>{item.status}</Td>
                    <Td>{item.model}</Td>
                    <Td className="max-w-md break-words">
                      {item.inputPreview}
                      {item.hasImage ? " · Screenshot" : ""}
                    </Td>
                    <Td>
                      <Button onClick={() => void inspect(item.id)}>Inspect</Button>
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>
          )}
          <Pager
            label={`${result.total} generations · Page ${result.page + 1}`}
            page={result.page}
            hasNext={hasNextPage(result.page, result.pageSize, result.total)}
            onPage={(page) => void load(page)}
          />
        </>
      ) : null}
    </>
  );
}
