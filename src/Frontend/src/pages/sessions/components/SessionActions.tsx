interface SessionActionsProps {
  canEdit?: boolean;
  disabled?: boolean;
  onEdit?: () => void;
  onRemove?: () => void;
}

const pill = "!h-8 !px-3.5 !text-xs disabled:opacity-60";

export function SessionActions({ canEdit = false, disabled = false, onEdit, onRemove }: SessionActionsProps) {
  return (
    <div className="inline-flex items-center gap-1.5">
      {canEdit && (
        <button type="button" data-action="edit" disabled={disabled} onClick={onEdit} className={`pdx-btn-accent ${pill}`}>
          Edit
        </button>
      )}
      <button type="button" data-action="remove" disabled={disabled} onClick={onRemove} className={`pdx-btn-danger ${pill}`}>
        Remove
      </button>
    </div>
  );
}
