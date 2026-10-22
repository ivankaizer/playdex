import { useEffect, useLayoutEffect, useRef, useState, type ChangeEvent, type ReactNode } from "react";
import { Cover } from "@/components/ui";
import { apiRequest, errorMessage } from "@/lib/api";
import { cx } from "@/lib/cx";
import { localDateOnly } from "@/lib/dates";
import { formatCount, formatCountNoun } from "@/lib/format";
import type { DraftAcceptRequest, DraftAcceptResponse, DraftBatchDto, DraftCapabilities, DraftRowDto } from "@/models/drafts";
import type { ManualMappingSearchResponse, ManualMappingSearchResult, ManualPlatform } from "@/models/manual";
import {
  acceptance,
  applySavedDraftRow,
  canAccept,
  createDraftRowEdit,
  draftDuration,
  durationCheck,
  hasGameSelection,
  hasValidDate,
  hasValidDuration,
  isDirty,
  isReady,
  markAccepted,
  needsPlatformReview,
  rowUpdate,
  setDraftTitle,
  type DraftRowEdit,
} from "../draftReviewState";
import { formatMinutes } from "../sessionCreateState";
import { delay, escapeDataString, newGuid, useLatestState } from "../support";
import {
  attachPaste,
  dataUrlBytes,
  draftTimezone,
  middleTruncate,
  readAsDataUrl,
  supportedImageTypes,
  validateImage,
} from "./draftInterop";
import { BoundSelect, ChangeInput } from "./FormControls";

export type DraftWorkspaceView = "compose" | "review";

interface DraftWorkspaceProps {
  capabilities?: DraftCapabilities | null;
  platforms?: readonly ManualPlatform[];
  embedded?: boolean;
  onClose?: () => void | Promise<void>;
  onAccepted?: () => void | Promise<void>;
  /** Short confirmation (e.g. "Hades · 1h added") shown as a toast by the host page. */
  onToast?: (message: string) => void;
  /** Fires when the workspace switches between the composer and a batch review. */
  onViewChange?: (view: DraftWorkspaceView) => void;
  /** Fires with the saved batch count after every reload of the review queue. */
  onBatchesChange?: (count: number) => void;
  /** Shows "Go to sessions" in the "Batch reviewed" state. */
  onGoToSessions?: () => void;
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
  /** Sessions added from the batch that just completed; null when no batch was completed. */
  reviewed: number | null;
  preview: string;
  mediaType: string;
  fileName: string;
  image: Uint8Array<ArrayBuffer> | null;
  busy: boolean;
  /** Automatic row saves in flight. */
  saving: number;
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
  reviewed: null,
  preview: "",
  mediaType: "",
  fileName: "screenshot.png",
  image: null,
  busy: false,
  saving: 0,
  generationSource: null,
  generationFailed: false,
});

/** Re-saves at most this many times when the row keeps changing while its PATCH is in flight. */
const MaxSavePasses = 3;

const reviewFieldClass = (needsReview: boolean) => (needsReview ? "border-danger" : "");

const ariaInvalid = (invalid: boolean) => (invalid ? "true" : "false");

/** Saved batches listed before "Show all". */
export const VisibleBatchCount = 5;

/** Count badge text, capped so it never outgrows its pill. */
export const badgeCount = (count: number) => (count > 99 ? "99+" : formatCount(count));

/** .NET en-US "g" format of a local time: "M/d/yyyy h:mm tt". */
export function formatShortDateTime(value: string): string {
  const date = new Date(value);
  const hours = date.getHours();
  const hour12 = hours % 12 === 0 ? 12 : hours % 12;
  return `${date.getMonth() + 1}/${date.getDate()}/${date.getFullYear()} ${hour12}:${String(date.getMinutes()).padStart(2, "0")} ${hours < 12 ? "AM" : "PM"}`;
}

export function DraftWorkspace({
  capabilities = null,
  platforms = [],
  embedded = false,
  onClose,
  onAccepted,
  onToast,
  onViewChange,
  onBatchesChange,
  onGoToSessions,
}: DraftWorkspaceProps) {
  const [state, get, update] = useLatestState(initialState);
  const [pasteId] = useState(() => `draft-paste-${newGuid(true)}`);
  const [showAllBatches, setShowAllBatches] = useState(false);
  const lifetime = useRef(new AbortController());
  const requestId = useRef(newGuid());
  const generatedBatchId = useRef<string | null>(null);
  const pasteArea = useRef<HTMLDivElement>(null);
  const saveQueue = useRef(new Map<string, Promise<boolean>>());
  const props = useRef({ capabilities, onClose, onAccepted, onToast, onViewChange, onBatchesChange });

  useLayoutEffect(() => {
    props.current = { capabilities, onClose, onAccepted, onToast, onViewChange, onBatchesChange };
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
      props.current.onBatchesChange?.(batches.length);
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

  const reviewing = state.batch !== null;
  useEffect(() => {
    props.current.onViewChange?.(reviewing ? "review" : "compose");
  }, [reviewing]);

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
      reviewed: null,
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

  const persistRow = async (id: string): Promise<boolean> => {
    for (let pass = 0; pass < MaxSavePasses; pass++) {
      const row = findRow(id);
      if (!row || row.isAccepted || !isDirty(row)) return true;
      const body = rowUpdate(row);
      try {
        const dto = await apiRequest<DraftRowDto>(rowPath(row), { method: "PATCH", body, signal: signal() });
        updateRow(id, (current) => applySavedDraftRow(current, body, dto));
      } catch (exception) {
        updateRow(id, (current) => ({
          ...current,
          error: `${errorMessage(exception)} Your edits are still here. Retry saving, or reopen this batch to review newer server changes.`,
        }));
        return false;
      }
    }
    const row = findRow(id);
    return !row || row.isAccepted || !isDirty(row);
  };

  /** Saves a row's edits; saves of the same row run one after another so each sends the latest revision. */
  const saveRow = (id: string): Promise<boolean> => {
    const previous = saveQueue.current.get(id) ?? Promise.resolve(true);
    const next = previous.then(() => persistRow(id));
    saveQueue.current.set(id, next);
    void next.finally(() => {
      if (saveQueue.current.get(id) === next) saveQueue.current.delete(id);
    });
    return next;
  };

  /** "Edits save automatically": runs after a field commits, without blocking the rest of the form. */
  const autoSave = async (id: string) => {
    const row = findRow(id);
    if (!row || row.isAccepted || !isDirty(row)) return;
    update((current) => ({ ...current, saving: current.saving + 1 }));
    try {
      await saveRow(id);
    } finally {
      update((current) => ({ ...current, saving: Math.max(0, current.saving - 1) }));
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
    const reviewed = get().rows.filter((row) => row.isAccepted).length;
    patch({ batch: null, rows: [], outcome, reviewed });
    await loadBatches();
  };

  const remove = async (id: string) => {
    patch({ busy: true });
    try {
      // Let an automatic save of this row finish first so the delete carries the current revision.
      await saveQueue.current.get(id);
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
      const added = saved.filter((row) => response.results.some((result) => result.id === row.id && result.sessionId != null));
      if (added.length > 0) props.current.onToast?.(acceptedMessage(added));
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
  // Ready rows go through "Accept all"; matched rows that still need a fix (date, duration, platform) count as "to check".
  const readyCount = rows.filter(isReady).length;
  const unmatchedCount = rows.filter((row) => !row.isAccepted && !hasGameSelection(row)).length;
  const toCheckCount = rows.filter((row) => !row.isAccepted && hasGameSelection(row) && !isReady(row)).length;
  const acceptedCount = rows.filter((row) => row.isAccepted).length;
  const shownBatches = showAllBatches ? state.batches : state.batches.slice(0, VisibleBatchCount);

  return (
    <div className={embedded ? "w-full" : "pdx-modal-backdrop fixed inset-0 z-[80] overflow-y-auto p-4"}>
      <section
        role={embedded ? undefined : "dialog"}
        aria-modal={embedded ? undefined : "true"}
        aria-label="AI session drafts"
        data-layout={embedded ? "extended-ai" : undefined}
        className={cx("flex w-full flex-col gap-7 text-ink", !embedded && "mx-auto my-8 max-w-5xl")}
      >
        {!embedded && (
          <div className="pdx-card flex flex-wrap items-start justify-between gap-4">
            <div>
              <p className="pdx-kicker">AI session import</p>
              <h2 className="pdx-title">Review session drafts</h2>
            </div>
            <button type="button" disabled={busy} onClick={() => void close()} className="pdx-btn-outline">
              Close
            </button>
          </div>
        )}

        {state.error.length > 0 && (
          <div
            role="alert"
            className="flex flex-wrap items-center justify-between gap-3 rounded-[20px] border border-danger-line bg-danger-tint px-5 py-4 text-sm text-danger"
          >
            <p>{state.error}</p>
            {state.generationFailed && state.generationSource !== null && capabilities?.available === true && (
              <button type="button" disabled={busy} onClick={() => void retryGeneration()} className="pdx-btn-danger">
                Retry generation
              </button>
            )}
          </div>
        )}

        {batch === null && state.reviewed !== null && state.outcome.length > 0 ? (
          <section className="pdx-card flex flex-col items-center gap-3 text-center">
            <span aria-hidden="true" className="grid h-14 w-14 place-items-center rounded-full bg-mint text-2xl text-accent">
              ✓
            </span>
            <h2 className="text-xl font-semibold">Batch reviewed</h2>
            <p role="status" className="max-w-[44em] text-sm text-muted">
              {state.reviewed > 0
                ? `${formatCountNoun(state.reviewed, "session")} added to your history.`
                : "Nothing from this batch was added."}
            </p>
            {onGoToSessions && (
              <button type="button" onClick={onGoToSessions} className="pdx-btn-primary mt-1.5 h-11">
                Go to sessions
              </button>
            )}
          </section>
        ) : (
          state.outcome.length > 0 && (
            <p role="status" className="rounded-[20px] border border-mint-line bg-mint px-5 py-4 text-sm text-accent">
              {state.outcome}
            </p>
          )
        )}

        {batch === null ? (
          <div className="grid items-start gap-7 min-[900px]:grid-cols-[minmax(0,2fr)_minmax(280px,1fr)]">
            <section className="pdx-card flex flex-col gap-[22px]">
              <div>
                <p className="pdx-kicker">Natural input</p>
                <h2 className="pdx-title">Create AI batch</h2>
                <p className="mt-1.5 max-w-[44em] text-sm leading-normal text-muted">
                  Describe one or several play sessions, add a screenshot, or use both. You will review every suggestion before it reaches your history.
                </p>
              </div>

              {capabilities?.available === true ? (
                <>
                  <label className="flex flex-col gap-2 text-xs font-medium text-muted">
                    What did you play?
                    <textarea
                      rows={9}
                      maxLength={capabilities.maxTextCharacters}
                      value={state.text}
                      onChange={(event) => patch({ text: event.currentTarget.value })}
                      disabled={busy}
                      placeholder="For example: Yesterday I played Hades II on PC for 1 hour 20 minutes."
                      className="pdx-input px-[22px] py-[18px] text-[15px] leading-relaxed"
                    ></textarea>
                    <span className="flex flex-wrap justify-between gap-x-4 gap-y-1 font-normal">
                      <span>Up to {formatCountNoun(capabilities.maxRows, "session")}. One session per line works best.</span>
                      <span aria-live="polite" className="pdx-num whitespace-nowrap">
                        {formatCount(state.text.length)} / {formatCount(capabilities.maxTextCharacters)}
                      </span>
                    </span>
                  </label>

                  <div
                    id={pasteId}
                    ref={pasteArea}
                    tabIndex={0}
                    aria-label="Screenshot drop area. Paste an image while focused."
                    className="flex flex-wrap items-center gap-4 rounded-tile border-[1.5px] border-dashed border-dash px-5 py-[18px] outline-none transition-colors focus:border-bar"
                  >
                    {state.image !== null ? (
                      <img
                        src={state.preview}
                        alt="Screenshot to be sent for extraction"
                        className="h-12 w-12 shrink-0 rounded-[14px] object-cover"
                      />
                    ) : (
                      <span aria-hidden="true" className="grid h-12 w-12 shrink-0 place-items-center rounded-[14px] bg-mint text-xl text-accent">
                        ▣
                      </span>
                    )}
                    <div className="min-w-0 flex-[1_1_200px]">
                      {/* File names truncate in the middle so the extension stays visible; the full name is in the title. */}
                      <p
                        title={state.image !== null ? state.fileName : undefined}
                        className="text-[15px] font-semibold [overflow-wrap:anywhere]"
                      >
                        {state.image !== null ? middleTruncate(state.fileName, 44) : "Add a screenshot"}
                      </p>
                      <p className="mt-0.5 text-[13px] text-muted">
                        {state.image !== null
                          ? "Attached · it will be read alongside your text."
                          : `PNG, JPEG or WebP up to ${Math.trunc(Math.trunc(capabilities.maxImageBytes / 1024) / 1024)} MiB. You can also paste an image while this area is focused.`}
                      </p>
                    </div>
                    {state.image !== null && (
                      <button type="button" disabled={busy} onClick={removeImage} className="pdx-btn-danger">
                        Remove screenshot
                      </button>
                    )}
                    <label className={cx("pdx-btn-outline focus-within:border-bar", busy && "pointer-events-none opacity-60")}>
                      Choose file
                      <input
                        type="file"
                        onChange={(event) => void loadImage(event)}
                        accept="image/png,image/jpeg,image/webp"
                        disabled={busy}
                        className="sr-only"
                      />
                    </label>
                  </div>

                  <div className="flex flex-wrap items-center justify-between gap-4 border-t border-divider pt-5">
                    <p className="flex-[1_1_300px] text-xs leading-[1.55] text-muted">
                      {`Text and screenshots are sent to ${capabilities.provider} for processing. Playdex retains your input, screenshot, and AI results ${retentionDays > 0 ? `for ${retentionDays} days` : "until deleted"} for administrator review to improve generation. Supported: PNG, JPEG, WebP up to ${Math.trunc(Math.trunc(capabilities.maxImageBytes / 1024) / 1024)} MiB and ${Math.trunc(capabilities.maxImagePixels / 1000000)} megapixels.`}
                    </p>
                    <button
                      type="button"
                      disabled={busy || (state.text.trim().length === 0 && state.image === null)}
                      onClick={() => void generate()}
                      className="pdx-btn-primary h-[50px] px-[26px] text-[15px]"
                    >
                      {busy ? "Generating…" : "Generate drafts"}
                    </button>
                  </div>
                </>
              ) : (
                <p className="pdx-dashed rounded-[20px] text-sm text-warn">
                  AI generation is currently unavailable. Saved drafts remain available for review, and you can use Manual entry.
                </p>
              )}
            </section>

            <aside className="pdx-card flex flex-col gap-[18px] !p-8 max-sm:!p-6">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="pdx-kicker">Review queue</p>
                  <h2 className="mt-2 text-[22px] font-semibold">Saved batches</h2>
                </div>
                {state.batches.length > 0 && (
                  <span className="pdx-num grid h-7 min-w-7 place-items-center rounded-full bg-mint px-2 text-[13px] font-bold text-accent">
                    {badgeCount(state.batches.length)}
                  </span>
                )}
              </div>

              {shownBatches.map((saved, index) => (
                <button
                  key={`${index}-${saved.id}`}
                  type="button"
                  disabled={busy}
                  onClick={() => void resume(saved.id)}
                  className="flex items-center gap-3 rounded-[20px] border border-[color:var(--tile-border)] bg-tile px-[18px] py-4 text-left transition-colors hover:border-mint-hover disabled:opacity-[.55]"
                >
                  <span className="min-w-0 flex-1">
                    <span className="block text-[15px] font-semibold [overflow-wrap:anywhere]">
                      Resume {formatShortDateTime(saved.createdAt)}
                    </span>
                    <span className="mt-0.5 block text-[13px] text-muted">
                      {formatCountNoun(saved.rows.filter((row) => row.status === "pending").length, "draft")} pending
                    </span>
                  </span>
                  <span aria-hidden="true" className="text-base text-accent">
                    →
                  </span>
                </button>
              ))}
              {state.batches.length > VisibleBatchCount && (
                <button
                  type="button"
                  aria-expanded={showAllBatches}
                  onClick={() => setShowAllBatches((current) => !current)}
                  className="pdx-btn-outline h-9 self-start px-4 text-[13px]"
                >
                  {showAllBatches ? "Show fewer" : `Show all ${formatCount(state.batches.length)}`}
                </button>
              )}
              {state.batches.length === 0 && <p className="pdx-dashed rounded-[20px] px-[18px] py-4 text-[13px]">Nothing waiting for review.</p>}
            </aside>
          </div>
        ) : (
          <>
            <section className="pdx-card flex flex-wrap items-end justify-between gap-5">
              <div className="min-w-0">
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => void back()}
                  className="text-[13px] text-muted transition-colors hover:text-accent disabled:cursor-not-allowed disabled:opacity-60"
                >
                  <span aria-hidden="true">← </span>Saved batches
                </button>
                <p className="pdx-kicker mt-3.5 [overflow-wrap:anywhere]">AI batch review · {formatShortDateTime(batch.createdAt)}</p>
                <h2 className="pdx-title">Review extracted sessions</h2>
                <div className="mt-3 flex flex-wrap gap-2">
                  {readyCount > 0 && <StatusChip className="bg-mint text-accent">{formatCount(readyCount)} ready</StatusChip>}
                  {toCheckCount > 0 && <StatusChip className="bg-tile text-warn">{formatCount(toCheckCount)} to check</StatusChip>}
                  {unmatchedCount > 0 && (
                    <StatusChip className="bg-danger-tint text-danger">
                      {formatCount(unmatchedCount)} {unmatchedCount === 1 ? "needs" : "need"} a game
                    </StatusChip>
                  )}
                  {acceptedCount > 0 && <StatusChip className="bg-tile text-ink-2">{formatCountNoun(acceptedCount, "session")} added</StatusChip>}
                </div>
              </div>
              <div className="flex flex-wrap gap-2.5">
                <button
                  type="button"
                  disabled={busy || capabilities?.available !== true}
                  onClick={() => void regenerate()}
                  className="pdx-btn-outline h-[46px]"
                >
                  Generate again
                </button>
                <button
                  type="button"
                  disabled={busy || readyCount === 0}
                  onClick={() => void accept(rows.filter(isReady).map((row) => row.id))}
                  className="pdx-btn-primary h-[46px] px-[22px]"
                >
                  Accept all ready ({formatCount(readyCount)})
                </button>
              </div>
            </section>

            <section aria-label="Session drafts" className="pdx-card flex flex-col gap-2.5 !p-5">
              <div className="flex justify-between gap-3 px-3 pb-1 pt-1.5 text-xs text-muted">
                <span>{formatCountNoun(rows.length, "draft")}</span>
                <span aria-live="polite">{state.saving > 0 ? "Saving…" : "Edits save automatically"}</span>
              </div>

              {rows.length === 0 && (
                <p className="pdx-dashed rounded-[20px] text-sm">
                  No eligible session evidence was found. Try describing individual plays with a date and duration.
                </p>
              )}

              {rows.map((row) => (
                <DraftRowArticle
                  key={row.id}
                  row={row}
                  busy={busy}
                  platforms={platforms}
                  onTitle={(title) => updateRow(row.id, (current) => setDraftTitle(current, title))}
                  onField={(changes) => {
                    updateRow(row.id, (current) => ({ ...current, ...changes }));
                    void autoSave(row.id);
                  }}
                  onCommitTitle={() => void autoSave(row.id)}
                  onSearch={() => void search(row.id)}
                  onRetrySave={() => void saveAction(row.id)}
                  onDelete={() => void remove(row.id)}
                  onAccept={() => void accept([row.id])}
                  onChoose={(result) => void choose(row.id, result)}
                />
              ))}
            </section>
          </>
        )}
      </section>
    </div>
  );
}

function acceptedMessage(rows: readonly DraftRowEdit[]): string {
  if (rows.length === 1) {
    const row = rows[0];
    const minutes = draftDuration(row);
    return `${row.selection?.title ?? row.title}${minutes !== null ? ` · ${formatMinutes(minutes)}` : ""} added`;
  }
  return `${formatCountNoun(rows.length, "session")} added to your history`;
}

function StatusChip({ className, children }: { className: string; children: ReactNode }) {
  return <span className={cx("pdx-num rounded-full px-3 py-1.5 text-xs font-semibold", className)}>{children}</span>;
}

interface DraftRowArticleProps {
  row: DraftRowEdit;
  busy: boolean;
  platforms: readonly ManualPlatform[];
  onTitle: (title: string) => void;
  onField: (changes: Partial<Pick<DraftRowEdit, "date" | "hours" | "minutes" | "platformKey">>) => void;
  onCommitTitle: () => void;
  onSearch: () => void;
  onRetrySave: () => void;
  onDelete: () => void;
  onAccept: () => void;
  onChoose: (result: ManualMappingSearchResult) => void;
}

const fieldLabelClass = "flex flex-col gap-1.5 text-[11px] font-medium uppercase tracking-[0.12em] text-muted";

function DraftRowArticle({
  row,
  busy,
  platforms,
  onTitle,
  onField,
  onCommitTitle,
  onSearch,
  onRetrySave,
  onDelete,
  onAccept,
  onChoose,
}: DraftRowArticleProps) {
  const [changing, setChanging] = useState(false);
  const selected = row.selection;
  const invalidGame = !hasGameSelection(row);
  const invalidDate = !hasValidDate(row);
  const invalidDuration = !hasValidDuration(row);
  const durationNote = durationCheck(row).note;
  const platformReview = needsPlatformReview(row);
  const showSearch = selected === null || changing || row.results.length > 0;

  return (
    <article
      data-draft-row
      className={cx(
        "rounded-tile border px-4 py-3.5",
        selected === null && !row.isAccepted
          ? "border-danger-line bg-danger-tint"
          : row.isAccepted
            ? "border-[color:var(--tile-border)] bg-tile"
            : invalidDuration
              ? "border-danger-line bg-tile"
              : "border-[color:var(--tile-border)] bg-tile",
      )}
    >
      <fieldset disabled={busy || row.isAccepted} className="m-0 min-w-0 border-0 p-0 disabled:opacity-[.55]">
        <legend className="sr-only">Session draft {row.title}</legend>
        <div data-draft-row-fields className="flex flex-wrap items-end gap-2.5">
          <div className="flex min-w-0 flex-[1_1_160px] items-center gap-3">
            <Cover title={selected?.title ?? row.title} src={selected?.capsuleUrl} className="h-12 w-12 rounded-[14px]" />
            {showSearch ? (
              <div className="flex min-w-0 flex-1 flex-col gap-1.5">
                <div className="flex gap-1.5">
                  <input
                    data-game-search
                    aria-label="Game match"
                    value={row.title}
                    onChange={(event) => onTitle(event.currentTarget.value)}
                    onBlur={onCommitTitle}
                    aria-invalid={ariaInvalid(invalidGame)}
                    placeholder="Search saved games and IGDB"
                    className={cx("pdx-input h-10 px-4", invalidGame ? "border-danger-line" : "")}
                  />
                  <button type="button" onClick={onSearch} className="pdx-btn-outline h-10 shrink-0 px-3.5 text-[13px]">
                    Find game
                  </button>
                </div>
                {selected === null ? (
                  <span className="text-xs font-medium text-danger">No verified game yet. Search to pick one.</span>
                ) : (
                  <span className="flex flex-wrap gap-x-2 text-xs text-muted">
                    <span className="min-w-0 truncate">Matched to {selected.title}. Search to pick a different game.</span>
                    {changing && row.results.length === 0 && (
                      <button type="button" onClick={() => setChanging(false)} className="font-semibold text-ink-2 underline">
                        Keep match
                      </button>
                    )}
                  </span>
                )}
              </div>
            ) : (
              <div data-draft-match className="min-w-0 flex-1">
                <p title={selected.title} className="truncate text-[15px] font-semibold">
                  {selected.title}
                </p>
                <p className="mt-[3px] flex min-w-0 flex-wrap items-center gap-x-2 gap-y-0.5 text-xs text-muted">
                  <span className="shrink-0 whitespace-nowrap font-semibold text-accent">✓ Matched</span>
                  <span title={row.title} className="min-w-0 flex-[1_1_120px] truncate">
                    from “{row.title}”
                  </span>
                  <span className="shrink-0 text-[10px] font-semibold uppercase tracking-[0.14em]">
                    {selected.source === "local" ? "Saved game" : "IGDB"}
                  </span>
                  {!row.isAccepted && (
                    <button type="button" onClick={() => setChanging(true)} className="shrink-0 font-semibold text-ink-2 underline">
                      Change
                    </button>
                  )}
                </p>
                {!row.isAccepted && durationNote.length > 0 && (
                  <p data-duration-note className="mt-1 text-xs font-medium text-danger">
                    {durationNote}
                  </p>
                )}
              </div>
            )}
          </div>

          <label className={cx(fieldLabelClass, "flex-[0_0_128px]")}>
            Date
            <ChangeInput
              type="date"
              max={localDateOnly(new Date())}
              value={row.date}
              onValueChange={(date) => onField({ date })}
              aria-invalid={ariaInvalid(invalidDate)}
              className={cx("pdx-input h-10 px-3 text-[13px] tracking-normal", reviewFieldClass(invalidDate))}
            />
          </label>

          <div className={cx(fieldLabelClass, "flex-[0_0_auto]")}>
            <span aria-hidden="true">Duration</span>
            <div
              className={cx(
                "flex h-10 items-center gap-1 rounded-full border bg-surface px-3 tracking-normal transition-colors focus-within:border-bar",
                invalidDuration ? "border-danger" : "border-line",
              )}
            >
              <ChangeInput
                aria-label="Hours"
                type="number"
                min="0"
                max="99"
                step="1"
                value={row.hours}
                onValueChange={(hours) => onField({ hours })}
                aria-invalid={ariaInvalid(invalidDuration)}
                className="pdx-num w-[34px] border-0 bg-transparent text-right text-sm font-semibold text-ink outline-none"
              />
              <span aria-hidden="true" className="text-xs">
                h
              </span>
              <ChangeInput
                aria-label="Minutes"
                type="number"
                min="0"
                max="59"
                step="1"
                value={row.minutes}
                onValueChange={(minutes) => onField({ minutes })}
                aria-invalid={ariaInvalid(invalidDuration)}
                className="pdx-num w-[42px] border-0 bg-transparent text-right text-sm font-semibold text-ink outline-none"
              />
              <span aria-hidden="true" className="text-xs">
                m
              </span>
            </div>
          </div>

          <label className={cx(fieldLabelClass, "flex-[0_0_136px]")}>
            Platform
            <BoundSelect
              value={row.platformKey}
              onValueChange={(platformKey) => onField({ platformKey })}
              data-needs-review={platformReview ? "" : undefined}
              className={cx("pdx-input h-10 pl-3 text-[13px] tracking-normal", reviewFieldClass(platformReview))}
            >
              {platforms.map((platform, index) => (
                <option key={`${index}-${platform.key}`} value={platform.key}>
                  {platform.name}
                </option>
              ))}
              {!platforms.some((platform) => platform.key === "unknown") && <option value="unknown">Unknown</option>}
            </BoundSelect>
          </label>

          <div className="ml-auto flex flex-[0_0_auto] items-center gap-1.5">
            {row.isAccepted ? (
              <span role="status" className="rounded-full bg-mint px-3.5 py-2 text-[13px] font-semibold text-accent">
                ✓ Added
              </span>
            ) : (
              <>
                <button
                  type="button"
                  aria-label="Delete draft"
                  title="Delete draft"
                  onClick={onDelete}
                  className="pdx-icon-btn h-10 w-10 border-danger-line text-sm font-semibold text-danger hover:border-danger hover:bg-danger-tint"
                >
                  ✕
                </button>
                <button
                  type="button"
                  disabled={!canAccept(row)}
                  onClick={onAccept}
                  className="pdx-btn-primary h-10 whitespace-nowrap px-4 text-[13px]"
                >
                  Accept
                </button>
              </>
            )}
          </div>

          {row.results.length > 0 && (
            <div className="grid basis-full grid-cols-[repeat(auto-fill,minmax(min(210px,100%),1fr))] gap-2 border-t border-divider pt-3">
              {row.results.map((result, index) => (
                <button
                  key={index}
                  type="button"
                  data-game-result
                  data-game-result-source={result.source}
                  className="grid min-w-0 grid-cols-[40px_minmax(0,1fr)] items-center gap-2.5 rounded-[18px] border-[1.5px] border-transparent bg-surface p-2 text-left transition-colors hover:border-mint-hover"
                  onClick={() => {
                    setChanging(false);
                    onChoose(result);
                  }}
                >
                  <Cover title={result.title} src={result.capsuleUrl} className="h-10 w-10 rounded-xl" initialClassName="text-sm" />
                  <span className="min-w-0">
                    <span title={result.title} className="line-clamp-2 text-sm font-medium leading-[1.3] [overflow-wrap:anywhere]">
                      {result.title}
                    </span>
                    <span
                      className={cx(
                        "block text-[10px] font-semibold uppercase tracking-[0.14em]",
                        result.source === "local" ? "text-accent" : "text-muted",
                      )}
                    >
                      {result.source === "local" ? "Saved game" : "From IGDB"}
                    </span>
                  </span>
                </button>
              ))}
            </div>
          )}
        </div>
      </fieldset>
      {row.error.length > 0 && (
        <div role="alert" className="mt-3 flex flex-wrap items-center justify-between gap-3 rounded-[18px] bg-surface px-4 py-3 text-sm text-danger">
          <p className="min-w-0 flex-1">{row.error}</p>
          {!row.isAccepted && isDirty(row) && (
            <button type="button" disabled={busy} onClick={onRetrySave} className="pdx-btn-danger h-9 px-4 text-[13px]">
              Retry save
            </button>
          )}
        </div>
      )}
    </article>
  );
}
