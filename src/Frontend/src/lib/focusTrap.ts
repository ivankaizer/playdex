import { useEffect, type KeyboardEvent as ReactKeyboardEvent, type RefObject } from "react";

const focusableSelector = 'a[href], button, input, select, textarea, [tabindex]:not([tabindex="-1"])';

/** What Tab can reach inside `container`, in order. */
export function focusableIn(container: HTMLElement): HTMLElement[] {
  return [...container.querySelectorAll<HTMLElement>(focusableSelector)].filter(
    (element) => !element.hasAttribute("disabled") && element.getAttribute("tabindex") !== "-1",
  );
}

/**
 * Keeps Tab and Shift+Tab inside a modal dialog: Tab on its last control goes to the first and Shift+Tab on the first
 * to the last, so keyboard focus never wanders to the page behind it. Call from the dialog's onKeyDown.
 */
export function trapTab(event: KeyboardEvent | ReactKeyboardEvent, container: HTMLElement | null): void {
  if (event.key !== "Tab" || container === null) return;
  const items = focusableIn(container);
  if (items.length === 0) {
    event.preventDefault();
    return;
  }
  const first = items[0];
  const last = items[items.length - 1];
  const active = document.activeElement;
  const outside = !(active instanceof Node) || !container.contains(active);
  if (event.shiftKey && (active === first || outside)) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && (active === last || outside)) {
    event.preventDefault();
    first.focus();
  }
}

/**
 * While `active`, moves focus into the dialog (its first form field, else its first control) and gives it back to
 * whatever had it when the dialog closes.
 */
export function useDialogFocus(dialog: RefObject<HTMLElement | null>, active = true): void {
  useEffect(() => {
    if (!active) return;
    const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const container = dialog.current;
    const first = container?.querySelector<HTMLElement>("input, select, textarea") ?? (container ? focusableIn(container)[0] : undefined);
    first?.focus();
    return () => {
      if (opener?.isConnected) opener.focus();
    };
  }, [dialog, active]);
}
