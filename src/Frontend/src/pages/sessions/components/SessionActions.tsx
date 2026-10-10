import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";

interface SessionActionsProps {
  /** Names the session in the compact menu's accessible label. */
  sessionTitle?: string;
  canEdit?: boolean;
  disabled?: boolean;
  onEdit?: () => void;
  onRemove?: () => void;
}

const pill = "!h-8 !px-3.5 !text-xs disabled:opacity-60";
const menuItem =
  "flex h-10 w-full items-center rounded-xl px-3.5 text-left text-sm font-semibold outline-none transition disabled:cursor-not-allowed disabled:opacity-60";

/**
 * Edit and Remove pills; at 600px and below they collapse into a "•••" menu so the title keeps the row.
 * The menu follows the WAI-ARIA menu button pattern: it opens on click, Enter, Space or ArrowDown (first item)
 * and ArrowUp (last item); arrows, Home and End move between items; Escape closes it and returns focus to
 * the button; Tab or a click outside closes it.
 */
export function SessionActions({ sessionTitle, canEdit = false, disabled = false, onEdit, onRemove }: SessionActionsProps) {
  const [open, setOpen] = useState(false);
  const [focusIndex, setFocusIndex] = useState(0);
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const itemRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const menuId = useId();

  const items = [
    ...(canEdit ? [{ key: "edit", label: "Edit", className: "text-accent hover:bg-mint focus-visible:bg-mint", onSelect: onEdit }] : []),
    { key: "remove", label: "Remove", className: "text-danger hover:bg-danger-tint focus-visible:bg-danger-tint", onSelect: onRemove },
  ];

  useEffect(() => {
    if (!open) return;
    itemRefs.current[focusIndex]?.focus();
  }, [open, focusIndex]);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener("pointerdown", onPointerDown);
    return () => document.removeEventListener("pointerdown", onPointerDown);
  }, [open]);

  useEffect(() => {
    if (disabled) setOpen(false);
  }, [disabled]);

  const openAt = (index: number) => {
    setFocusIndex(index);
    setOpen(true);
  };

  const close = (returnFocus: boolean) => {
    setOpen(false);
    if (returnFocus) triggerRef.current?.focus();
  };

  const onTriggerKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      openAt(0);
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      openAt(items.length - 1);
    }
  };

  const onMenuKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const last = items.length - 1;
    let next: number | null = null;
    if (event.key === "ArrowDown") next = focusIndex >= last ? 0 : focusIndex + 1;
    else if (event.key === "ArrowUp") next = focusIndex <= 0 ? last : focusIndex - 1;
    else if (event.key === "Home") next = 0;
    else if (event.key === "End") next = last;
    else if (event.key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      close(true);
      return;
    } else if (event.key === "Tab") {
      close(false);
      return;
    }
    if (next === null) return;
    event.preventDefault();
    setFocusIndex(next);
  };

  const select = (onSelect?: () => void) => {
    close(true);
    onSelect?.();
  };

  return (
    <>
      <div className="inline-flex items-center gap-1.5 max-[600px]:hidden">
        {canEdit && (
          <button type="button" data-action="edit" disabled={disabled} onClick={onEdit} className={`pdx-btn-accent ${pill}`}>
            Edit
          </button>
        )}
        <button type="button" data-action="remove" disabled={disabled} onClick={onRemove} className={`pdx-btn-danger ${pill}`}>
          Remove
        </button>
      </div>

      <div ref={rootRef} data-session-menu className="relative hidden max-[600px]:block">
        <button
          ref={triggerRef}
          type="button"
          data-action="menu"
          aria-haspopup="menu"
          aria-expanded={open ? "true" : "false"}
          aria-controls={open ? menuId : undefined}
          aria-label={sessionTitle ? `Actions for ${sessionTitle}` : "Session actions"}
          title="Session actions"
          disabled={disabled}
          onClick={() => (open ? close(false) : openAt(0))}
          onKeyDown={onTriggerKeyDown}
          className="pdx-icon-btn !h-9 !w-9 text-sm font-bold tracking-[0.05em] disabled:cursor-not-allowed disabled:opacity-60"
        >
          <span aria-hidden="true">•••</span>
        </button>
        {open && (
          <div
            id={menuId}
            role="menu"
            aria-label={sessionTitle ? `Actions for ${sessionTitle}` : "Session actions"}
            onKeyDown={onMenuKeyDown}
            className="absolute right-0 top-[42px] z-10 flex min-w-[150px] flex-col gap-0.5 rounded-[18px] border border-line bg-surface p-1.5 shadow-pop"
          >
            {items.map((item, index) => (
              <button
                key={item.key}
                ref={(element) => {
                  itemRefs.current[index] = element;
                }}
                type="button"
                role="menuitem"
                data-menu-action={item.key}
                tabIndex={index === focusIndex ? 0 : -1}
                onClick={() => select(item.onSelect)}
                className={`${menuItem} ${item.className}`}
              >
                {item.label}
              </button>
            ))}
          </div>
        )}
      </div>
    </>
  );
}
