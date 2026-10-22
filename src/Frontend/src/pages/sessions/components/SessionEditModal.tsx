import { useEffect, useId, useRef } from "react";
import type { DurationCheck } from "../sessionsState";

interface SessionEditModalProps {
  gameTitle: string;
  startedAt: string;
  /** Duration as typed, e.g. "1h 30m". */
  duration: string;
  /** Validation for `duration`: zero and "75m"-style input are rejected, over 24h asks for confirmation. */
  durationCheck?: DurationCheck;
  isSaving?: boolean;
  error?: string;
  onStartedAtChanged?: (value: string) => void;
  onDurationChanged?: (value: string) => void;
  onSave?: () => void;
  onCancel?: () => void;
}

const fieldLabel = "flex flex-col gap-1.5 text-xs font-medium text-muted";
const validDuration: DurationCheck = { minutes: null, ok: true, message: "" };

export function SessionEditModal({
  gameTitle,
  startedAt,
  duration,
  durationCheck = validDuration,
  isSaving = false,
  error = "",
  onStartedAtChanged,
  onDurationChanged,
  onSave,
  onCancel,
}: SessionEditModalProps) {
  const durationHintId = useId();
  const startedRef = useRef<HTMLInputElement>(null);
  const returnFocus = useRef<Element | null>(null);

  // Focus the first field on open and give focus back to whatever opened the dialog on close.
  useEffect(() => {
    returnFocus.current = document.activeElement;
    startedRef.current?.focus();
    return () => {
      if (returnFocus.current instanceof HTMLElement && returnFocus.current.isConnected) returnFocus.current.focus();
    };
  }, []);

  const durationMessage = duration.trim().length > 0 ? durationCheck.message : "";
  const durationInvalid = !durationCheck.ok && durationMessage.length > 0;
  const canSave = durationCheck.ok && !isSaving;

  return (
    <div
      className="pdx-modal-backdrop fixed inset-0 z-[70] grid place-items-center p-6"
      onClick={(event) => {
        if (event.target === event.currentTarget && !isSaving) onCancel?.();
      }}
      onKeyDown={(event) => {
        if (event.key === "Escape" && !isSaving) onCancel?.();
      }}
    >
      <section
        role="dialog"
        aria-modal="true"
        aria-labelledby="session-edit-title"
        className="pdx-modal flex w-[min(460px,100%)] flex-col gap-4 text-ink"
      >
        <div className="flex items-start justify-between gap-4">
          <div className="min-w-0">
            <p className="pdx-kicker">Session</p>
            <h2 id="session-edit-title" className="mt-2 text-[22px] font-semibold leading-tight">
              Edit session
            </h2>
          </div>
          <button
            type="button"
            aria-label="Close session editor"
            disabled={isSaving}
            onClick={onCancel}
            className="pdx-icon-btn text-base disabled:cursor-not-allowed disabled:opacity-60"
          >
            ×
          </button>
        </div>

        <div className={fieldLabel}>
          Game
          <p
            title={gameTitle}
            className="flex h-11 items-center rounded-full border border-line bg-tile px-[18px] text-sm font-medium text-ink"
          >
            <span className="truncate">{gameTitle}</span>
          </p>
        </div>

        <form
          className="flex flex-col gap-4"
          onSubmit={(event) => {
            event.preventDefault();
            if (canSave) onSave?.();
          }}
        >
          <label className={fieldLabel}>
            Started
            <input
              ref={startedRef}
              type="datetime-local"
              step="1"
              value={startedAt}
              disabled={isSaving}
              onChange={(event) => onStartedAtChanged?.(event.currentTarget.value)}
              className="pdx-input !h-11 !px-3.5"
            />
          </label>
          <label className={fieldLabel}>
            Duration
            <input
              type="text"
              inputMode="text"
              autoComplete="off"
              placeholder="1h 30m"
              value={duration}
              disabled={isSaving}
              aria-invalid={durationInvalid ? "true" : undefined}
              aria-describedby={durationHintId}
              onChange={(event) => onDurationChanged?.(event.currentTarget.value)}
              className="pdx-input !h-11"
            />
            <span
              id={durationHintId}
              aria-live="polite"
              data-duration-hint={durationInvalid ? "error" : undefined}
              className="min-h-4 text-xs text-danger"
            >
              {durationMessage}
            </span>
          </label>

          {error.length > 0 && error !== durationMessage && (
            <p role="alert" className="text-sm text-danger">
              {error}
            </p>
          )}

          <div className="mt-2 flex flex-wrap justify-end gap-2.5">
            <button type="button" data-action="cancel" disabled={isSaving} onClick={onCancel} className="pdx-btn-outline !h-11">
              Cancel
            </button>
            <button type="submit" data-action="save" disabled={!canSave} className="pdx-btn-primary !h-11 !px-[22px]">
              {isSaving ? "Saving…" : "Save session"}
            </button>
          </div>
        </form>
      </section>
    </div>
  );
}
