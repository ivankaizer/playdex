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
    <div className="fixed inset-0 z-[70] flex items-center justify-center bg-slate-950/65 p-4">
      <section
        role="dialog"
        aria-modal="true"
        aria-labelledby="session-edit-title"
        className="w-full max-w-lg rounded-3xl border border-slate-200 bg-white p-5 shadow-2xl dark:border-slate-700 dark:bg-slate-900 sm:p-6"
      >
        <div className="flex items-start justify-between gap-4">
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">Edit session</p>
            <h2 id="session-edit-title" className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
              {gameTitle}
            </h2>
          </div>
          <button
            type="button"
            aria-label="Close session editor"
            disabled={isSaving}
            onClick={onCancel}
            className="rounded-full border border-slate-300 px-3 py-1 text-sm text-slate-600 transition hover:border-emerald-400 disabled:cursor-not-allowed disabled:opacity-60 dark:border-slate-700 dark:text-slate-300"
          >
            ×
          </button>
        </div>

        <form
          className="mt-6 space-y-4"
          onSubmit={(event) => {
            event.preventDefault();
            onSave?.();
          }}
        >
          <label className="block text-xs font-medium text-slate-600 dark:text-slate-300">
            Started
            <input
              type="datetime-local"
              step="1"
              value={startedAt}
              disabled={isSaving}
              onChange={(event) => onStartedAtChanged?.(event.currentTarget.value)}
              className="mt-2 w-full rounded-xl border border-slate-300 bg-white/90 px-3 py-2 text-sm text-slate-900 outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30 disabled:opacity-60 dark:border-slate-700 dark:bg-slate-950/80 dark:text-white"
            />
          </label>
          <label className="block text-xs font-medium text-slate-600 dark:text-slate-300">
            Ended
            <input
              type="datetime-local"
              step="1"
              value={endedAt}
              disabled={isSaving}
              onChange={(event) => onEndedAtChanged?.(event.currentTarget.value)}
              className="mt-2 w-full rounded-xl border border-slate-300 bg-white/90 px-3 py-2 text-sm text-slate-900 outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30 disabled:opacity-60 dark:border-slate-700 dark:bg-slate-950/80 dark:text-white"
            />
          </label>

          {error.length > 0 && (
            <p role="alert" className="text-sm text-rose-700 dark:text-rose-300">
              {error}
            </p>
          )}

          <div className="flex justify-end gap-3 pt-2">
            <button
              type="button"
              data-action="cancel"
              disabled={isSaving}
              onClick={onCancel}
              className="rounded-full border border-slate-300 px-4 py-2 text-xs font-semibold uppercase tracking-[0.2em] text-slate-700 transition hover:border-emerald-400 disabled:cursor-not-allowed disabled:opacity-60 dark:border-slate-700 dark:text-slate-200"
            >
              Cancel
            </button>
            <button
              type="submit"
              data-action="save"
              disabled={isSaving}
              className="rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.2em] text-slate-950 transition hover:bg-emerald-300 disabled:cursor-not-allowed disabled:opacity-60"
            >
              {isSaving ? "Saving..." : "Save changes"}
            </button>
          </div>
        </form>
      </section>
    </div>
  );
}
