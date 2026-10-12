import { isAbortError } from "@/lib/api";

/** True when the error is the cancellation of the component lifetime (Blazor's `_lifetime`). */
export function isLifetimeCancellation(error: unknown, lifetime: AbortSignal): boolean {
  return isAbortError(error) && lifetime.aborted;
}
