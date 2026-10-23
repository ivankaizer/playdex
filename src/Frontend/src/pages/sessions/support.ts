import { useCallback, useRef, useState } from "react";

/** Equivalent to .NET `Uri.EscapeDataString` (RFC 3986: only unreserved characters stay literal). */
export function escapeDataString(value: string): string {
  return encodeURIComponent(value).replace(/[!'()*]/g, (character) => `%${character.charCodeAt(0).toString(16).toUpperCase()}`);
}

/** A request controller that is aborted together with its parent (CancellationTokenSource.CreateLinkedTokenSource). */
export function createLinkedController(parent: AbortSignal): AbortController {
  const controller = new AbortController();
  if (parent.aborted) {
    controller.abort();
  } else {
    parent.addEventListener("abort", () => controller.abort(), { once: true });
  }
  return controller;
}

/** Abortable `Task.Delay`. */
export function delay(milliseconds: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal.aborted) {
      reject(new DOMException("The operation was aborted.", "AbortError"));
      return;
    }
    const timer = setTimeout(() => {
      signal.removeEventListener("abort", onAbort);
      resolve();
    }, milliseconds);
    const onAbort = () => {
      clearTimeout(timer);
      reject(new DOMException("The operation was aborted.", "AbortError"));
    };
    signal.addEventListener("abort", onAbort, { once: true });
  });
}

/** C# `Guid.NewGuid().ToString()` (and "N" format when `compact`). */
export function newGuid(compact = false): string {
  const id = crypto.randomUUID();
  return compact ? id.replace(/-/g, "") : id;
}

/**
 * React state that can also be read synchronously after awaits (mirrors the mutable C# state classes
 * that the Blazor code-behind reads between awaits).
 */
export function useLatestState<T>(initial: () => T): [T, () => T, (updater: (current: T) => T) => T] {
  const [state, setState] = useState(initial);
  const ref = useRef(state);
  const get = useCallback(() => ref.current, []);
  const update = useCallback((updater: (current: T) => T) => {
    ref.current = updater(ref.current);
    setState(ref.current);
    return ref.current;
  }, []);
  return [state, get, update];
}
