import { useEffect, useState, type ReactNode } from "react";
import { adminApi, isAbortError, type GenerationDetail } from "../api";
import { formatLocal } from "../time";
import { Alert, Badge, linkClass, Panel, rowClass, Table, Td, Th } from "./ui";

const preClass =
  "mt-2 max-h-96 overflow-auto whitespace-pre-wrap break-words rounded-[18px] border border-[var(--tile-border)] bg-tile p-4 font-mono text-xs leading-relaxed text-ink-2";

const summaryClass =
  "pdx-hit cursor-pointer rounded-full text-sm font-semibold text-ink-2 hover:text-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-bar";

/** Generation outcome pill: green for success, red for failures, neutral otherwise. */
export function OutcomeBadge({ status }: { status: string }) {
  const tone = status === "success" ? "success" : status === "running" || status === "cancelled" ? "neutral" : "danger";
  return <Badge tone={tone}>{status}</Badge>;
}

/** The screenshot needs the bearer token, so it is fetched as a blob and shown through an object URL. */
function useScreenshot(id: string, enabled: boolean): { url: string | null; failed: boolean } {
  const [state, setState] = useState<{ url: string | null; failed: boolean }>({ url: null, failed: false });
  useEffect(() => {
    if (!enabled) return;
    const controller = new AbortController();
    let objectUrl: string | null = null;
    setState({ url: null, failed: false });
    adminApi
      .generationImage(id, { signal: controller.signal })
      .then((blob) => {
        if (controller.signal.aborted) return;
        objectUrl = URL.createObjectURL(blob);
        setState({ url: objectUrl, failed: false });
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted && !isAbortError(error)) setState({ url: null, failed: true });
      });
    return () => {
      controller.abort();
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [id, enabled]);
  return state;
}

export function GenerationDetails({ detail, actions }: { detail: GenerationDetail; actions?: ReactNode }) {
  const screenshot = useScreenshot(detail.id, detail.hasImage);

  return (
    <Panel label="Generation details" kicker="Generation details" title={detail.email} actions={actions}>
      <div className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center gap-2 text-sm text-ink-2">
          <OutcomeBadge status={detail.status} />
          <span className="pdx-num">{formatLocal(detail.createdAt)}</span>
          <span aria-hidden="true" className="text-muted">·</span>
          <span>
            {detail.provider} / {detail.model}
          </span>
        </div>
        <p className="text-sm text-ink-2">
          Reference date: {detail.referenceDate} · {detail.timezone}
        </p>
        <small className="break-all text-xs text-muted">
          Request: {detail.requestId} · Generation: {detail.id}
        </small>
      </div>
      {detail.error !== null ? <Alert tone="danger">{detail.error}</Alert> : null}
      <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(400px,100%),1fr))]">
        <section className="pdx-tile flex min-w-0 flex-col gap-2 !px-5 !py-5">
          <h3 className="pdx-label">User input</h3>
          <pre className={preClass}>
            {detail.inputText === null || detail.inputText.trim() === "" ? "No text submitted." : detail.inputText}
          </pre>
          {detail.hasImage ? (
            <div className="mt-2">
              {screenshot.url !== null ? (
                <>
                  <a className={linkClass + " text-sm"} href={screenshot.url} target="_blank" rel="noopener">
                    Open original screenshot
                  </a>
                  <img
                    className="mt-3 max-h-[32rem] max-w-full rounded-[18px] border border-line"
                    src={screenshot.url}
                    alt="Original screenshot submitted for session extraction"
                  />
                </>
              ) : screenshot.failed ? (
                <p role="alert" className="text-sm text-danger">The screenshot could not be loaded.</p>
              ) : (
                <p className="text-sm text-muted">Loading screenshot…</p>
              )}
            </div>
          ) : null}
        </section>
        <section className="pdx-tile flex min-w-0 flex-col gap-2 !px-5 !py-5">
          <h3 className="pdx-label">Generated result</h3>
          <p className="text-sm text-muted">The initial result, before user edits.</p>
          {detail.rows.length > 0 ? (
            <Table label="Generated sessions">
              <thead>
                <tr>
                  <Th>Game</Th>
                  <Th>Date</Th>
                  <Th>Minutes</Th>
                  <Th>Platform</Th>
                </tr>
              </thead>
              <tbody>
                {detail.rows.map((row, index) => (
                  <tr key={index} className={rowClass}>
                    <Td className="font-semibold text-ink">{row.title}</Td>
                    <Td className="pdx-num whitespace-nowrap">{row.date}</Td>
                    <Td className="pdx-num">{row.durationMinutes}</Td>
                    <Td>{row.platformKey}</Td>
                  </tr>
                ))}
              </tbody>
            </Table>
          ) : null}
          {detail.result === null ? (
            <p className="text-sm text-ink-2">No batch was generated.</p>
          ) : (
            <details>
              <summary className={summaryClass}>Full initial result</summary>
              <pre className={preClass}>{detail.result}</pre>
            </details>
          )}
        </section>
      </div>
      <section className="flex flex-col gap-2">
        <h3 className="pdx-label">Provider responses</h3>
        {detail.responses.length === 0 ? <p className="text-sm text-ink-2">No response body was received.</p> : null}
        {detail.responses.map((response, index) => (
          <details key={index} className="rounded-[18px] border border-divider px-4 py-3">
            <summary className={summaryClass}>
              Attempt {index + 1} · HTTP {response.httpStatus}
              {response.truncated ? " · Response truncated at 1 MiB" : ""}
            </summary>
            <pre className={preClass}>{response.body}</pre>
          </details>
        ))}
      </section>
    </Panel>
  );
}
