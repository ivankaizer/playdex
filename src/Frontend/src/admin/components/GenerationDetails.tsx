import { useEffect, useState } from "react";
import { adminApi, isAbortError, type GenerationDetail } from "../api";
import { formatLocal } from "../time";
import { Alert, Card, rowClass, Table, Td, Th } from "./ui";

const preClass =
  "max-h-96 overflow-auto whitespace-pre-wrap break-words rounded-md bg-slate-50 p-3 font-mono text-xs text-slate-800 dark:bg-slate-800 dark:text-slate-200";

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

export function GenerationDetails({ detail }: { detail: GenerationDetail }) {
  const screenshot = useScreenshot(detail.id, detail.hasImage);

  return (
    <Card>
      <div className="flex flex-col gap-0.5 text-sm">
        <strong className="text-base">{detail.email}</strong>
        <span>
          {detail.status} · {formatLocal(detail.createdAt)}
        </span>
        <span>
          {detail.provider} / {detail.model}
        </span>
        <span>
          Reference date: {detail.referenceDate} · {detail.timezone}
        </span>
        <small className="text-slate-500 dark:text-slate-400">
          Request: {detail.requestId} · Generation: {detail.id}
        </small>
      </div>
      {detail.error !== null ? <Alert tone="danger">{detail.error}</Alert> : null}
      <div className="mt-4 grid gap-6 lg:grid-cols-2">
        <section className="min-w-0">
          <h3 className="mb-2 font-semibold">User input</h3>
          <pre className={preClass}>
            {detail.inputText === null || detail.inputText.trim() === "" ? "No text submitted." : detail.inputText}
          </pre>
          {detail.hasImage ? (
            <div className="mt-3">
              {screenshot.url !== null ? (
                <>
                  <a className="text-sm text-emerald-600 hover:underline" href={screenshot.url} target="_blank" rel="noopener">
                    Open original screenshot
                  </a>
                  <img
                    className="mt-2 max-h-[32rem] rounded-md border border-slate-200 dark:border-slate-700"
                    src={screenshot.url}
                    alt="Original screenshot submitted for session extraction"
                  />
                </>
              ) : screenshot.failed ? (
                <p className="text-sm text-rose-600">The screenshot could not be loaded.</p>
              ) : (
                <p className="text-sm text-slate-500">Loading screenshot…</p>
              )}
            </div>
          ) : null}
        </section>
        <section className="min-w-0">
          <h3 className="mb-2 font-semibold">Generated result</h3>
          <p className="mb-2 text-sm text-slate-500 dark:text-slate-400">The initial result, before user edits.</p>
          {detail.rows.length > 0 ? (
            <Table>
              <thead>
                <tr>
                  <Th>Game</Th>
                  <Th>Date</Th>
                  <Th>Minutes</Th>
                  <Th>Platform</Th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 dark:divide-slate-800">
                {detail.rows.map((row, index) => (
                  <tr key={index} className={rowClass}>
                    <Td>{row.title}</Td>
                    <Td>{row.date}</Td>
                    <Td>{row.durationMinutes}</Td>
                    <Td>{row.platformKey}</Td>
                  </tr>
                ))}
              </tbody>
            </Table>
          ) : null}
          {detail.result === null ? (
            <p className="mt-2 text-sm">No batch was generated.</p>
          ) : (
            <details className="mt-2">
              <summary className="cursor-pointer text-sm font-medium">Full initial result</summary>
              <pre className={preClass}>{detail.result}</pre>
            </details>
          )}
        </section>
      </div>
      <h3 className="mb-2 mt-6 font-semibold">Provider responses</h3>
      {detail.responses.length === 0 ? <p className="text-sm">No response body was received.</p> : null}
      {detail.responses.map((response, index) => (
        <details key={index} className="mb-2">
          <summary className="cursor-pointer text-sm font-medium">
            Attempt {index + 1} · HTTP {response.httpStatus}
            {response.truncated ? " · Response truncated at 1 MiB" : ""}
          </summary>
          <pre className={preClass}>{response.body}</pre>
        </details>
      ))}
    </Card>
  );
}
