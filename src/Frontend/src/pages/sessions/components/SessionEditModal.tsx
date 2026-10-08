interface SessionEditModalProps {
  gameTitle: string;
  startedAt: string;
  endedAt: string;
  isSaving?: boolean;
  error?: string;
  onStartedAtChanged?: (value: string) => void;
  onEndedAtChanged?: (value: string) => void;
  onSave?: () => void;
  onCancel?: () => void;
}

const fieldLabel = "flex flex-col gap-1.5 text-xs font-medium text-muted";

export function SessionEditModal({
  gameTitle,
  startedAt,
  endedAt,
  isSaving = false,
  error = "",
  onStartedAtChanged,
  onEndedAtChanged,
  onSave,
  onCancel,
}: SessionEditModalProps) {
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
          <p className="flex h-11 items-center truncate rounded-full border border-line bg-tile px-[18px] text-sm font-medium text-ink">
            {gameTitle}
          </p>
        </div>

        <form
          className="flex flex-col gap-4"
          onSubmit={(event) => {
            event.preventDefault();
            onSave?.();
          }}
        >
          <div className="flex flex-col gap-4">
            <label className={fieldLabel}>
              Started
              <input
                type="datetime-local"
                step="1"
                value={startedAt}
                disabled={isSaving}
                onChange={(event) => onStartedAtChanged?.(event.currentTarget.value)}
                className="pdx-input !h-11 !px-3.5"
              />
            </label>
            <label className={fieldLabel}>
              Ended
              <input
                type="datetime-local"
                step="1"
                value={endedAt}
                disabled={isSaving}
                onChange={(event) => onEndedAtChanged?.(event.currentTarget.value)}
                className="pdx-input !h-11 !px-3.5"
              />
            </label>
          </div>

          {error.length > 0 && (
            <p role="alert" className="text-sm text-danger">
              {error}
            </p>
          )}

          <div className="mt-2 flex flex-wrap justify-end gap-2.5">
            <button type="button" data-action="cancel" disabled={isSaving} onClick={onCancel} className="pdx-btn-outline !h-11">
              Cancel
            </button>
            <button type="submit" data-action="save" disabled={isSaving} className="pdx-btn-primary !h-11 !px-[22px]">
              {isSaving ? "Saving..." : "Save session"}
            </button>
          </div>
        </form>
      </section>
    </div>
  );
}
