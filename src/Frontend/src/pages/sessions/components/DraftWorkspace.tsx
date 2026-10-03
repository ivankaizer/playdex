import { useEffect, useLayoutEffect, useRef, useState, type ChangeEvent } from "react";
import { Breadcrumbs } from "@/components/Breadcrumbs";
import { apiRequest, errorMessage } from "@/lib/api";
import type { DraftAcceptRequest, DraftAcceptResponse, DraftBatchDto, DraftCapabilities, DraftRowDto } from "@/models/drafts";
import type { ManualMappingSearchResponse, ManualMappingSearchResult, ManualPlatform } from "@/models/manual";
import {
  acceptance,
  applyDraftRow,
  canAccept,
  createDraftRowEdit,
  hasGameSelection,
  hasValidDate,
  hasValidDuration,
  isDirty,
  markAccepted,
  needsPlatformReview,
  rowUpdate,
  setDraftTitle,
  type DraftRowEdit,
} from "../draftReviewState";
import { delay, escapeDataString, newGuid, useLatestState } from "../support";
import { attachPaste, dataUrlBytes, draftTimezone, readAsDataUrl, supportedImageTypes, validateImage } from "./draftInterop";
import { BoundSelect, ChangeInput } from "./FormControls";
import "./DraftWorkspace.css";

interface DraftWorkspaceProps {
  capabilities?: DraftCapabilities | null;
  platforms?: readonly ManualPlatform[];
  embedded?: boolean;
  onClose?: () => void | Promise<void>;
  onAccepted?: () => void | Promise<void>;
}

interface GenerationSource {
  text: string;
  image: Uint8Array<ArrayBuffer> | null;
  mediaType: string;
  fileName: string;
  timezone: string;
}

interface WorkspaceState {
  batches: readonly DraftBatchDto[];
  batch: DraftBatchDto | null;
  rows: readonly DraftRowEdit[];
  text: string;
  error: string;
  outcome: string;
  preview: string;
  mediaType: string;
  fileName: string;
  image: Uint8Array<ArrayBuffer> | null;
  busy: boolean;
  generationSource: GenerationSource | null;
  generationFailed: boolean;
}

const initialState = (): WorkspaceState => ({
  batches: [],
  batch: null,
  rows: [],
  text: "",
  error: "",
  outcome: "",
  preview: "",
  mediaType: "",
  fileName: "screenshot.png",
  image: null,
  busy: false,
  generationSource: null,
  generationFailed: false,
});

/** Marks elements that Blazor's scoped CSS would stamp with the component's b-* attribute. */
const scope = { "data-dw-scope": "" };

const reviewFieldClass = (needsReview: boolean) =>
  needsReview
    ? "border-rose-500 bg-rose-50 focus:border-rose-600 focus:ring-2 focus:ring-rose-200 dark:border-rose-400 dark:bg-rose-950/30 dark:focus:border-rose-300 dark:focus:ring-rose-900"
    : "border-slate-300 bg-white focus:border-emerald-400 dark:border-slate-700 dark:bg-slate-950";

const ariaInvalid = (invalid: boolean) => (invalid ? "true" : "false");

/** .NET en-US "g" format of a local time: "M/d/yyyy h:mm tt". */
export function formatShortDateTime(value: string): string {
  const date = new Date(value);
  const hours = date.getHours();
  const hour12 = hours % 12 === 0 ? 12 : hours % 12;
  return `${date.getMonth() + 1}/${date.getDate()}/${date.getFullYear()} ${hour12}:${String(date.getMinutes()).padStart(2, "0")} ${hours < 12 ? "AM" : "PM"}`;
}

export function DraftWorkspace({ capabilities = null, platforms = [], embedded = false, onClose, onAccepted }: DraftWorkspaceProps) {
  const [state, get, update] = useLatestState(initialState);
  const [pasteId] = useState(() => `draft-paste-${newGuid(true)}`);
  const lifetime = useRef(new AbortController());
  const requestId = useRef(newGuid());
  const generatedBatchId = useRef<string | null>(null);
  const pasteArea = useRef<HTMLDivElement>(null);
  const props = useRef({ capabilities, onClose, onAccepted });

  useLayoutEffect(() => {
    props.current = { capabilities, onClose, onAccepted };
  });

  const patch = (changes: Partial<WorkspaceState>) => update((current) => ({ ...current, ...changes }));
  const findRow = (id: string) => get().rows.find((row) => row.id === id);
  const updateRow = (id: string, change: (row: DraftRowEdit) => DraftRowEdit) =>
    update((current) => ({ ...current, rows: current.rows.map((row) => (row.id === id ? change(row) : row)) }));
  const signal = () => lifetime.current.signal;

  const loadBatches = async () => {
    const lifetimeSignal = signal();
    try {
      const batches = await apiRequest<DraftBatchDto[]>("/manual/draft-batches", { signal: lifetimeSignal });
      patch({ batches });
    } catch (exception) {
      if (lifetimeSignal.aborted) return;
      patch({ error: errorMessage(exception) });
    }
  };

  useEffect(() => {
    if (lifetime.current.signal.aborted) lifetime.current = new AbortController();
    const controller = lifetime.current;
    void loadBatches();
    // OnInitializedAsync; the cleanup mirrors DisposeAsync (cancel in-flight work).
    return () => controller.abort();
  }, []);

  const composerVisible = state.batch === null && capabilities?.available === true;
  const maxImageBytes = capabilities?.maxImageBytes;
  const maxImagePixels = capabilities?.maxImagePixels;
  useEffect(() => {
    const element = pasteArea.current;
    if (!composerVisible || !element || maxImageBytes === undefined || maxImagePixels === undefined) return;
    return attachPaste(element, { pasteImage, pasteError }, maxImageBytes, maxImagePixels);
  }, [composerVisible, maxImageBytes, maxImagePixels]);

  const show = (batch: DraftBatchDto) =>
    patch({
      outcome: "",
      batch,
      rows: batch.rows.filter((row) => row.status !== "deleted").map(createDraftRowEdit),
    });

  const resume = async (id: string) => {
    patch({ busy: true, error: "", generationFailed: false });
    try {
      show(await apiRequest<DraftBatchDto>(`/manual/draft-batches/${id}`, { signal: signal() }));
    } catch (exception) {
      patch({ error: errorMessage(exception) });
    } finally {
      patch({ busy: false });
    }
  };

  const rowPath = (row: DraftRowEdit) => `/manual/draft-batches/${get().batch?.id ?? ""}/rows/${row.id}`;

  const saveRow = async (id: string): Promise<boolean> => {
    const row = findRow(id);
    if (!row || row.isAccepted || !isDirty(row)) return true;
    try {
      const dto = await apiRequest<DraftRowDto>(rowPath(row), { method: "PATCH", body: rowUpdate(row), signal: signal() });
      updateRow(id, (current) => applyDraftRow(current, dto));
      return true;
    } catch (exception) {
      updateRow(id, (current) => ({
        ...current,
        error: `${errorMessage(exception)} Your edits are still here. Retry saving, or reopen this batch to review newer server changes.`,
      }));
      return false;
    }
  };

  const saveAll = async (): Promise<boolean> => {
    const pending = get()
      .rows.filter((row) => !row.isAccepted)
      .map((row) => row.id);
    for (const id of pending) {
      if (findRow(id)?.isAccepted) continue;
      if (!(await saveRow(id))) return false;
    }
    return true;
  };

  const close = async () => {
    patch({ busy: true });
    try {
      if (await saveAll()) await props.current.onClose?.();
    } finally {
      patch({ busy: false });
    }
  };

  const back = async () => {
    patch({ busy: true });
    try {
      if (!(await saveAll())) return;
      patch({ batch: null, rows: [], generationFailed: false, error: "" });
      await loadBatches();
    } finally {
      patch({ busy: false });
    }
  };

  const loadImage = async (event: ChangeEvent<HTMLInputElement>) => {
    const caps = props.current.capabilities;
    const file = event.currentTarget.files?.[0];
    if (!caps || get().busy) return;
    try {
      if (!file) throw new Error("No file was supplied.");
      if (!supportedImageTypes.includes(file.type)) throw new Error("Choose a PNG, JPEG or WebP screenshot.");
      if (file.size > caps.maxImageBytes) {
        throw new Error(`Supplied file with size ${file.size} bytes exceeds the maximum of ${caps.maxImageBytes} bytes.`);
      }
      const preview = await readAsDataUrl(file);
      const bytes = dataUrlBytes(preview);
      if (!(await validateImage(preview, caps.maxImagePixels))) throw new Error("The image is invalid or exceeds the pixel limit.");
      patch({ image: bytes, mediaType: file.type, fileName: file.name, preview, error: "" });
    } catch (exception) {
      patch({ error: errorMessage(exception) });
    }
  };

  function pasteImage(dataUrl: string, mediaType: string) {
    const caps = props.current.capabilities;
    if (get().busy || !caps) return;
    try {
      const bytes = dataUrlBytes(dataUrl);
      if (bytes.length > caps.maxImageBytes || !supportedImageTypes.includes(mediaType)) {
        throw new Error("Unsupported or oversized screenshot.");
      }
      patch({ image: bytes, mediaType, fileName: "pasted-screenshot", preview: dataUrl, error: "" });
    } catch (exception) {
      patch({ error: errorMessage(exception) });
    }
  }

  function pasteError(error: string) {
    patch({ error });
  }

  const removeImage = () => patch({ image: null, preview: "" });

  const createBatch = async (buildContent: () => FormData): Promise<DraftBatchDto> => {
    const lifetimeSignal = signal();
    const send = () =>
      apiRequest<DraftBatchDto>("/manual/draft-batches", { method: "POST", body: buildContent, signal: lifetimeSignal });
    try {
      return await send();
    } catch (exception) {
      // HttpRequestException == a browser transport failure (fetch rejects with TypeError).
      if (!(exception instanceof TypeError) || lifetimeSignal.aborted) throw exception;
      await delay(300, lifetimeSignal);
      return await send();
    }
  };

  const runGeneration = async () => {
    const source = get().generationSource;
    if (source === null) return;
    patch({ busy: true, error: "", generationFailed: false });
    try {
      const buildContent = () => {
        const content = new FormData();
        content.append("requestId", requestId.current);
        content.append("timezone", source.timezone);
        if (source.text.trim().length > 0) content.append("text", source.text);
        if (source.image !== null) {
          content.append("file", new Blob([source.image], { type: source.mediaType }), source.fileName);
        }
        return content;
      };
      const batch = await createBatch(buildContent);
      show(batch);
      generatedBatchId.current = batch.id;
      patch({ text: "" });
      removeImage();
    } catch (exception) {
      patch({ error: errorMessage(exception), generationFailed: true });
    } finally {
      patch({ busy: false });
    }
  };

  const generate = async () => {
    const caps = props.current.capabilities;
    if (get().busy || caps?.available !== true) return;
    patch({ busy: true, error: "", generationFailed: false });
    try {
      const current = get();
      if (current.text.length > caps.maxTextCharacters) throw new Error("Text exceeds the character limit.");
      const timezone = draftTimezone();
      requestId.current = newGuid();
      patch({
        generationSource: {
          text: current.text,
          image: current.image,
          mediaType: current.mediaType,
          fileName: current.fileName,
          timezone,
        },
      });
      await runGeneration();
    } catch (exception) {
      patch({ error: errorMessage(exception) });
    } finally {
      patch({ busy: false });
    }
  };

  const retryGeneration = async () => {
    const current = get();
    if (current.busy || !current.generationFailed || current.generationSource === null || props.current.capabilities?.available !== true) {
      return;
    }
    await runGeneration();
  };

  const regenerate = async () => {
    if (get().busy || props.current.capabilities?.available !== true) return;
    patch({ busy: true, error: "", generationFailed: false });
    try {
      if (!(await saveAll())) return;
      if (get().generationSource === null || generatedBatchId.current !== (get().batch?.id ?? null)) {
        patch({
          batch: null,
          rows: [],
          text: "",
          outcome: "Add the original description or screenshot to generate again. Saved batches cannot reload the original input.",
        });
        removeImage();
        await loadBatches();
        return;
      }
      requestId.current = newGuid();
      await runGeneration();
    } finally {
      patch({ busy: false });
    }
  };

  const saveAction = async (id: string) => {
    patch({ busy: true });
    try {
      await saveRow(id);
    } finally {
      patch({ busy: false });
    }
  };

  const search = async (id: string) => {
    patch({ busy: true });
    try {
      if (!(await saveRow(id))) return;
      const title = findRow(id)?.title ?? "";
      const result = await apiRequest<ManualMappingSearchResponse>(`/manual/search?query=${escapeDataString(title)}`, {
        signal: signal(),
      });
      updateRow(id, (row) => ({
        ...row,
        results: result.results,
        error: !result.igdbAvailable
          ? "IGDB is unavailable. You can still choose a saved game."
          : result.results.length === 0
            ? "No matches. Correct the title and search again, or delete this draft."
            : "",
      }));
    } catch (exception) {
      updateRow(id, (row) => ({ ...row, error: errorMessage(exception) }));
    } finally {
      patch({ busy: false });
    }
  };

  const choose = async (id: string, selection: ManualMappingSearchResult) => {
    patch({ busy: true });
    try {
      updateRow(id, (row) => ({ ...row, selection }));
      if (await saveRow(id)) updateRow(id, (row) => ({ ...row, results: [] }));
    } finally {
      patch({ busy: false });
    }
  };

  const completeBatch = async (outcome: string) => {
    patch({ batch: null, rows: [], outcome });
    await loadBatches();
  };

  const remove = async (id: string) => {
    patch({ busy: true });
    try {
      const row = findRow(id);
      if (row) {
        await apiRequest<unknown>(`${rowPath(row)}?revision=${row.revision}`, { method: "DELETE", signal: signal() });
        update((current) => ({ ...current, rows: current.rows.filter((item) => item.id !== id) }));
        if (!get().rows.some((item) => !item.isAccepted)) {
          await completeBatch("Draft batch completed; it was removed from saved review.");
        }
      }
    } catch (exception) {
      updateRow(id, (row) => ({ ...row, error: errorMessage(exception) }));
    } finally {
      patch({ busy: false });
    }
  };

  const accept = async (ids: readonly string[]) => {
    if (get().busy) return;
    patch({ busy: true, error: "" });
    try {
      const saved: DraftRowEdit[] = [];
      for (const id of ids) {
        const row = findRow(id);
        if (!row || !canAccept(row)) continue;
        if (!(await saveRow(id))) continue;
        const current = findRow(id);
        if (current && canAccept(current)) saved.push(current);
      }
      if (saved.length === 0) return;
      const body: DraftAcceptRequest = { rows: saved.map(acceptance) };
      const response = await apiRequest<DraftAcceptResponse>(`/manual/draft-batches/${get().batch?.id ?? ""}/accept`, {
        method: "POST",
        body,
        signal: signal(),
      });
      for (const result of response.results) {
        if (!findRow(result.id)) continue;
        const sessionId = result.sessionId;
        updateRow(result.id, (row) =>
          sessionId != null ? markAccepted(row, sessionId) : { ...row, error: result.error ?? "Acceptance failed. Retry this row." },
        );
      }
      if (response.results.some((result) => result.sessionId != null)) await props.current.onAccepted?.();
      if (!get().rows.some((row) => !row.isAccepted)) {
        await completeBatch("All ready drafts were accepted; the batch was removed from saved review.");
      }
    } catch (exception) {
      patch({ error: errorMessage(exception) });
    } finally {
      patch({ busy: false });
    }
  };

  const { busy, batch, rows } = state;
  const retentionDays = capabilities?.historyRetentionDays ?? 30;

  return (
    <div className={embedded ? "w-full" : "fixed inset-0 z-[80] overflow-y-auto bg-slate-950/65 p-4"}>
      <section
        role={embedded ? undefined : "dialog"}
        aria-modal={embedded ? undefined : "true"}
        aria-label="AI session drafts"
        data-layout={embedded ? "extended-ai" : undefined}
        className={
          embedded
            ? "w-full text-slate-900 dark:text-white"
            : "mx-auto my-8 w-full max-w-5xl rounded-3xl bg-slate-50 p-5 text-slate-900 shadow-xl dark:bg-slate-950 dark:text-white sm:p-6"
        }
      >
        {!embedded && (
          <div className="mb-5 flex items-start justify-between gap-4 rounded-3xl border border-slate-200 bg-white p-5 dark:border-slate-800 dark:bg-slate-900">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">AI session import</p>
              <h2 className="mt-2 text-xl font-semibold">Review session drafts</h2>
            </div>
            <button
              {...scope}
              disabled={busy}
              onClick={() => void close()}
              className="rounded-full border border-slate-300 px-4 py-2 text-xs font-semibold uppercase tracking-[0.2em] dark:border-slate-700"
            >
              Close
            </button>
          </div>
        )}

        {state.error.length > 0 && (
          <div
            role="alert"
            className="mb-5 flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-rose-200 bg-rose-50 p-4 text-sm text-rose-700 dark:border-rose-400/20 dark:bg-rose-400/10 dark:text-rose-200"
          >
            <p>{state.error}</p>
            {state.generationFailed && state.generationSource !== null && capabilities?.available === true && (
              <button
                {...scope}
                type="button"
                disabled={busy}
                onClick={() => void retryGeneration()}
                className="shrink-0 rounded-full border border-rose-300 px-4 py-2 text-xs font-semibold uppercase tracking-[0.16em] disabled:opacity-60 dark:border-rose-400/40"
              >
                Retry generation
              </button>
            )}
          </div>
        )}
        {state.outcome.length > 0 && (
          <p
            role="status"
            className="mb-5 rounded-2xl border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-800 dark:border-emerald-400/20 dark:bg-emerald-400/10 dark:text-emerald-200"
          >
            {state.outcome}
          </p>
        )}

        {batch === null ? (
          <div className="grid items-start gap-5 lg:grid-cols-[minmax(0,1fr)_20rem]">
            <section className="rounded-3xl border border-slate-200 bg-white/80 p-5 dark:border-slate-800 dark:bg-slate-900/60 sm:p-6">
              <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">Natural input</p>
              <h2 className="mt-2 text-xl font-semibold">Create AI batch</h2>
              <p className="mt-2 max-w-2xl text-sm text-slate-600 dark:text-slate-300">
                Describe one or several play sessions, add a screenshot, or use both. You will review every suggestion before it reaches your history.
              </p>

              {capabilities?.available === true ? (
                <>
                  <div className="mt-6">
                    <label className="block text-sm font-semibold text-slate-800 dark:text-slate-100">
                      What did you play?
                      <textarea
                        rows={7}
                        maxLength={capabilities.maxTextCharacters}
                        value={state.text}
                        onChange={(event) => patch({ text: event.currentTarget.value })}
                        disabled={busy}
                        placeholder="For example: Yesterday I played Hades II on PC for 1 hour 20 minutes."
                        className="mt-2 block w-full resize-y rounded-2xl border border-slate-300 bg-white px-4 py-3 text-sm font-normal text-slate-900 outline-none placeholder:text-slate-400 focus:border-emerald-400 dark:border-slate-700 dark:bg-slate-950 dark:text-white"
                      ></textarea>
                    </label>
                    <p className="mt-2 text-xs text-slate-500 dark:text-slate-400">
                      Up to {capabilities.maxTextCharacters} characters and {capabilities.maxRows} sessions.
                    </p>
                  </div>

                  <div
                    id={pasteId}
                    ref={pasteArea}
                    tabIndex={0}
                    className="mt-5 rounded-2xl border border-dashed border-slate-300 bg-slate-50 p-4 outline-none focus:border-emerald-400 dark:border-slate-700 dark:bg-slate-950/70"
                  >
                    <label className="block text-sm font-semibold text-slate-800 dark:text-slate-100">
                      Add a screenshot
                      <span className="mt-1 block text-xs font-normal text-slate-500 dark:text-slate-400">
                        Choose a file or paste an image while this area is focused.
                      </span>
                      <input
                        type="file"
                        onChange={(event) => void loadImage(event)}
                        accept="image/png,image/jpeg,image/webp"
                        disabled={busy}
                        className="mt-3 block w-full text-xs text-slate-500 file:mr-3 file:rounded-full file:border-0 file:bg-emerald-400 file:px-4 file:py-2 file:text-xs file:font-semibold file:text-slate-950 dark:text-slate-400"
                      />
                    </label>
                    {state.image !== null && (
                      <div className="mt-4 flex flex-col items-start gap-3 rounded-2xl border border-slate-200 bg-white p-3 dark:border-slate-800 dark:bg-slate-900 sm:flex-row">
                        <img
                          src={state.preview}
                          alt="Screenshot to be sent for extraction"
                          className="max-h-48 max-w-full rounded-xl object-contain sm:max-w-xs"
                        />
                        <button
                          {...scope}
                          type="button"
                          disabled={busy}
                          onClick={removeImage}
                          className="rounded-full border border-slate-300 px-4 py-2 text-xs font-semibold text-slate-700 dark:border-slate-700 dark:text-slate-200"
                        >
                          Remove screenshot
                        </button>
                      </div>
                    )}
                  </div>

                  <div className="mt-5 flex flex-col gap-4 border-t border-slate-200 pt-5 dark:border-slate-800 sm:flex-row sm:items-center sm:justify-between">
                    <p className="max-w-2xl text-xs leading-5 text-slate-500 dark:text-slate-400">
                      {`Text and screenshots are sent to ${capabilities.provider} for processing. Playdex retains your input, screenshot, and AI results ${retentionDays > 0 ? `for ${retentionDays} days` : "until deleted"} for administrator review to improve generation. Supported: PNG, JPEG, WebP up to ${Math.trunc(Math.trunc(capabilities.maxImageBytes / 1024) / 1024)} MiB and ${Math.trunc(capabilities.maxImagePixels / 1000000)} megapixels.`}
                    </p>
                    <button
                      {...scope}
                      type="button"
                      disabled={busy || (state.text.trim().length === 0 && state.image === null)}
                      onClick={() => void generate()}
                      className="shrink-0 rounded-full bg-emerald-400 px-5 py-2.5 text-xs font-semibold uppercase tracking-[0.18em] text-slate-950 disabled:opacity-60"
                    >
                      {busy ? "Generating drafts…" : "Generate drafts"}
                    </button>
                  </div>
                </>
              ) : (
                <div className="mt-6 rounded-2xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800 dark:border-amber-400/20 dark:bg-amber-400/10 dark:text-amber-200">
                  AI generation is currently unavailable. Saved drafts remain available for review, and you can use the Manual session tab.
                </div>
              )}
            </section>

            <aside className="rounded-3xl border border-slate-200 bg-white/80 p-5 dark:border-slate-800 dark:bg-slate-900/60 lg:sticky lg:top-6">
              <div className="flex items-center justify-between gap-3">
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.25em] text-slate-500 dark:text-slate-400">Review queue</p>
                  <h2 className="mt-2 text-lg font-semibold">Saved batches</h2>
                </div>
                {state.batches.length > 0 && (
                  <span className="rounded-full bg-emerald-100 px-2.5 py-1 text-xs font-semibold text-emerald-800 dark:bg-emerald-400/15 dark:text-emerald-200">
                    {state.batches.length}
                  </span>
                )}
              </div>

              <div className="mt-4 space-y-2">
                {state.batches.map((saved, index) => (
                  <button
                    {...scope}
                    key={`${index}-${saved.id}`}
                    type="button"
                    disabled={busy}
                    onClick={() => void resume(saved.id)}
                    className="w-full rounded-2xl border border-slate-200 bg-slate-50 p-3 text-left transition hover:border-emerald-400 hover:bg-emerald-50 dark:border-slate-700 dark:bg-slate-950/70 dark:hover:bg-emerald-400/10"
                  >
                    <span className="block text-sm font-semibold">Resume {formatShortDateTime(saved.createdAt)}</span>
                    <span className="mt-1 block text-xs text-slate-500 dark:text-slate-400">
                      {saved.rows.filter((row) => row.status === "pending").length} pending
                    </span>
                  </button>
                ))}
                {state.batches.length === 0 && (
                  <div className="rounded-2xl bg-slate-100 p-4 text-sm text-slate-500 dark:bg-slate-950 dark:text-slate-400">
                    <p className="font-semibold text-slate-700 dark:text-slate-200">No saved drafts.</p>
                    <p className="mt-1 text-xs leading-5">Generated batches appear here until every pending row is accepted or deleted.</p>
                  </div>
                )}
              </div>
            </aside>
          </div>
        ) : (
          <>
            <div className="rounded-3xl border border-slate-200 bg-white/80 p-5 dark:border-slate-800 dark:bg-slate-900/60 sm:p-6">
              <Breadcrumbs parentLabel="Saved batches" currentLabel="Review batch" onParent={() => void back()} disabled={busy} />
              <div className="mt-4 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">AI batch review</p>
                  <h2 className="mt-2 text-xl font-semibold">Review extracted sessions</h2>
                  <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">
                    Correct the details and choose a verified game before accepting each session.
                  </p>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  <button
                    {...scope}
                    type="button"
                    disabled={busy || capabilities?.available !== true}
                    onClick={() => void regenerate()}
                    className="shrink-0 rounded-full border border-slate-300 px-4 py-2 text-xs font-semibold uppercase tracking-[0.16em] text-slate-700 disabled:opacity-60 dark:border-slate-700 dark:text-slate-200"
                  >
                    Generate again
                  </button>
                  <button
                    {...scope}
                    type="button"
                    disabled={busy || !rows.some(canAccept)}
                    onClick={() => void accept(rows.filter(canAccept).map((row) => row.id))}
                    className="shrink-0 rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.16em] text-slate-950 disabled:opacity-60"
                  >
                    Accept all ready
                  </button>
                </div>
              </div>
            </div>

            {rows.length === 0 && (
              <p className="mt-5 rounded-3xl border border-slate-200 bg-white/80 p-6 text-sm text-slate-600 dark:border-slate-800 dark:bg-slate-900/60 dark:text-slate-300">
                No eligible session evidence was found. Try describing individual plays with a date and duration.
              </p>
            )}

            <div className="mt-5 space-y-3">
              {rows.map((row) => (
                <DraftRowArticle
                  key={row.id}
                  row={row}
                  busy={busy}
                  platforms={platforms}
                  onTitle={(title) => updateRow(row.id, (current) => setDraftTitle(current, title))}
                  onField={(changes) => updateRow(row.id, (current) => ({ ...current, ...changes }))}
                  onSearch={() => void search(row.id)}
                  onSave={() => void saveAction(row.id)}
                  onDelete={() => void remove(row.id)}
                  onAccept={() => void accept([row.id])}
                  onChoose={(result) => void choose(row.id, result)}
                />
              ))}
            </div>
          </>
        )}
      </section>
    </div>
  );
}

interface DraftRowArticleProps {
  row: DraftRowEdit;
  busy: boolean;
  platforms: readonly ManualPlatform[];
  onTitle: (title: string) => void;
  onField: (changes: Partial<Pick<DraftRowEdit, "date" | "hours" | "minutes" | "platformKey">>) => void;
  onSearch: () => void;
  onSave: () => void;
  onDelete: () => void;
  onAccept: () => void;
  onChoose: (result: ManualMappingSearchResult) => void;
}

function DraftRowArticle({ row, busy, platforms, onTitle, onField, onSearch, onSave, onDelete, onAccept, onChoose }: DraftRowArticleProps) {
  const selected = row.selection;
  const invalidGame = !hasGameSelection(row);
  const invalidDate = !hasValidDate(row);
  const invalidDuration = !hasValidDuration(row);
  const platformReview = needsPlatformReview(row);

  return (
    <article data-draft-row className="rounded-2xl border border-slate-200 bg-white/80 p-4 dark:border-slate-800 dark:bg-slate-900/60">
      <fieldset {...scope} disabled={busy || row.isAccepted}>
        <div className="flex items-center justify-between gap-3">
          <p className="text-[10px] font-semibold uppercase tracking-[0.25em] text-emerald-700 dark:text-emerald-300">Session draft</p>
          {row.isAccepted && (
            <span className="rounded-full bg-emerald-100 px-2.5 py-1 text-[10px] font-semibold uppercase tracking-[0.16em] text-emerald-800 dark:bg-emerald-400/15 dark:text-emerald-200">
              Accepted
            </span>
          )}
        </div>

        <div
          data-draft-row-fields
          className="mt-3 grid gap-3 md:grid-cols-2 xl:grid-cols-[minmax(18rem,2fr)_minmax(9rem,0.8fr)_minmax(9rem,0.8fr)_minmax(14rem,1.2fr)_auto] xl:items-end"
        >
          <div className="md:col-span-2 xl:col-span-1">
            <label className="block text-xs font-medium text-slate-600 dark:text-slate-300">
              Game match
              <span className="mt-1 flex items-center gap-2">
                <input
                  data-game-search
                  value={row.title}
                  onChange={(event) => onTitle(event.currentTarget.value)}
                  aria-invalid={ariaInvalid(invalidGame)}
                  placeholder="Search saved games and IGDB"
                  className={`block min-w-0 flex-1 rounded-xl border px-3 py-2 text-sm text-slate-900 outline-none dark:text-white ${reviewFieldClass(invalidGame)}`}
                />
                <button
                  {...scope}
                  type="button"
                  onClick={onSearch}
                  className="shrink-0 rounded-xl border border-slate-300 px-3 py-2 text-xs font-semibold text-slate-700 transition hover:border-emerald-400 dark:border-slate-700 dark:text-slate-200"
                >
                  Find game
                </button>
              </span>
            </label>
            {selected !== null && (
              <div className="mt-2 flex min-w-0 items-center gap-2 rounded-xl border border-emerald-300/60 bg-emerald-50 px-2 py-1.5 dark:border-emerald-400/30 dark:bg-emerald-400/10">
                {selected.capsuleUrl !== null ? (
                  <img src={selected.capsuleUrl} alt={`${selected.title} poster`} className="h-10 w-8 shrink-0 rounded-md object-cover" />
                ) : (
                  <span
                    aria-hidden="true"
                    className="flex h-10 w-8 shrink-0 items-center justify-center rounded-md bg-slate-200 text-[7px] uppercase text-slate-500 dark:bg-slate-800 dark:text-slate-400"
                  >
                    No art
                  </span>
                )}
                <span className="min-w-0">
                  <span className="block truncate text-sm font-semibold">{selected.title}</span>
                  <span className="block text-[10px] font-semibold uppercase tracking-[0.16em] text-emerald-700 dark:text-emerald-300">
                    Matched from {selected.source === "local" ? "saved game" : "IGDB"}
                  </span>
                </span>
              </div>
            )}
          </div>

          <label className="block text-xs font-medium text-slate-600 dark:text-slate-300">
            Date
            <ChangeInput
              type="date"
              value={row.date}
              onValueChange={(date) => onField({ date })}
              aria-invalid={ariaInvalid(invalidDate)}
              className={`mt-1 block w-full rounded-xl border px-3 py-2 text-sm outline-none dark:text-white ${reviewFieldClass(invalidDate)}`}
            />
          </label>

          <div>
            <span className="block text-xs font-medium text-slate-600 dark:text-slate-300">Duration</span>
            <div className="mt-1 grid grid-cols-2 gap-2">
              <label className="sr-only" htmlFor={`draft-hours-${row.id}`}>
                Hours
              </label>
              <ChangeInput
                id={`draft-hours-${row.id}`}
                aria-label="Hours"
                type="number"
                min="0"
                step="1"
                value={row.hours}
                onValueChange={(hours) => onField({ hours })}
                aria-invalid={ariaInvalid(invalidDuration)}
                className={`block w-full rounded-xl border px-2 py-2 text-sm outline-none dark:text-white ${reviewFieldClass(invalidDuration)}`}
              />
              <label className="sr-only" htmlFor={`draft-minutes-${row.id}`}>
                Minutes
              </label>
              <ChangeInput
                id={`draft-minutes-${row.id}`}
                aria-label="Minutes"
                type="number"
                min="0"
                step="1"
                value={row.minutes}
                onValueChange={(minutes) => onField({ minutes })}
                aria-invalid={ariaInvalid(invalidDuration)}
                className={`block w-full rounded-xl border px-2 py-2 text-sm outline-none dark:text-white ${reviewFieldClass(invalidDuration)}`}
              />
            </div>
          </div>

          <label className="block text-xs font-medium text-slate-600 dark:text-slate-300">
            Platform
            <BoundSelect
              value={row.platformKey}
              onValueChange={(platformKey) => onField({ platformKey })}
              data-needs-review={platformReview ? "" : undefined}
              className={`mt-1 block w-full rounded-xl border px-3 py-2 text-sm outline-none dark:text-white ${reviewFieldClass(platformReview)}`}
            >
              {platforms.map((platform, index) => (
                <option key={`${index}-${platform.key}`} value={platform.key}>
                  {platform.name}
                </option>
              ))}
              {!platforms.some((platform) => platform.key === "unknown") && <option value="unknown">Unknown</option>}
            </BoundSelect>
          </label>

          <div className="flex flex-wrap items-center gap-2 md:col-span-2 xl:col-span-1 xl:justify-end">
            <button
              {...scope}
              type="button"
              onClick={onSave}
              className="rounded-xl border border-slate-300 px-3 py-2 text-[10px] font-semibold uppercase tracking-[0.12em] text-slate-700 dark:border-slate-700 dark:text-slate-200"
            >
              Save edits
            </button>
            <button
              {...scope}
              type="button"
              onClick={onDelete}
              className="rounded-xl border border-rose-200 px-3 py-2 text-[10px] font-semibold uppercase tracking-[0.12em] text-rose-700 dark:border-rose-400/30 dark:text-rose-200"
            >
              Delete draft
            </button>
            <button
              {...scope}
              type="button"
              disabled={!canAccept(row)}
              onClick={onAccept}
              className="rounded-xl bg-emerald-400 px-3 py-2 text-[10px] font-semibold uppercase tracking-[0.14em] text-slate-950 disabled:opacity-60"
            >
              Accept
            </button>
          </div>

          {row.results.length > 0 && (
            <div className="grid max-h-56 gap-2 overflow-y-auto border-t border-slate-200 pt-3 md:col-span-2 sm:grid-cols-2 lg:grid-cols-3 xl:col-span-5 xl:grid-cols-4 dark:border-slate-800">
              {row.results.map((result, index) => (
                <button
                  {...scope}
                  key={index}
                  type="button"
                  data-game-result
                  data-game-result-source={result.source}
                  className="flex min-w-0 items-center gap-2 rounded-xl border border-slate-200 bg-white p-2 text-left transition hover:border-emerald-400 dark:border-slate-700 dark:bg-slate-950"
                  onClick={() => onChoose(result)}
                >
                  {result.capsuleUrl !== null ? (
                    <img src={result.capsuleUrl} alt={`${result.title} poster`} className="h-12 w-9 shrink-0 rounded-md object-cover" />
                  ) : (
                    <span
                      aria-hidden="true"
                      className="flex h-12 w-9 shrink-0 items-center justify-center rounded-md bg-slate-200 text-[7px] uppercase text-slate-500 dark:bg-slate-800 dark:text-slate-400"
                    >
                      No art
                    </span>
                  )}
                  <span className="min-w-0">
                    <span className="block truncate text-sm font-semibold">{result.title}</span>
                    <span className="text-[10px] font-semibold uppercase tracking-[0.14em] text-slate-500 dark:text-slate-400">
                      {result.source === "local" ? "Saved game" : "IGDB"}
                    </span>
                  </span>
                </button>
              ))}
            </div>
          )}
        </div>
      </fieldset>
      {row.error.length > 0 && (
        <p role="alert" className="mt-4 rounded-xl bg-rose-50 p-3 text-sm text-rose-700 dark:bg-rose-400/10 dark:text-rose-200">
          {row.error}
        </p>
      )}
      {row.isAccepted && (
        <p role="status" className="mt-4 rounded-xl bg-emerald-50 p-3 text-sm text-emerald-800 dark:bg-emerald-400/10 dark:text-emerald-200">
          Accepted — session saved.
        </p>
      )}
    </article>
  );
}
