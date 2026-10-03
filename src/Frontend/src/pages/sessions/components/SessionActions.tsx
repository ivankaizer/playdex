interface SessionActionsProps {
  canEdit?: boolean;
  disabled?: boolean;
  onEdit?: () => void;
  onRemove?: () => void;
}

export function SessionActions({ canEdit = false, disabled = false, onEdit, onRemove }: SessionActionsProps) {
  return (
    <div className="inline-flex items-center gap-2">
      {canEdit && (
        <button
          type="button"
          data-action="edit"
          disabled={disabled}
          onClick={onEdit}
          className="rounded-full border border-emerald-500/60 px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.2em] text-emerald-700 transition hover:border-emerald-400 disabled:cursor-not-allowed disabled:opacity-60 dark:text-emerald-200"
        >
          Edit
        </button>
      )}
      <button
        type="button"
        data-action="remove"
        disabled={disabled}
        onClick={onRemove}
        className="rounded-full border border-rose-500/60 px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.2em] text-rose-700 transition hover:border-rose-400 disabled:cursor-not-allowed disabled:opacity-60 dark:text-rose-200"
      >
        Remove
      </button>
    </div>
  );
}
