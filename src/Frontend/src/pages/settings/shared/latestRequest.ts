/**
 * Tracks the most recent request of a kind: starting a new one (or invalidating) aborts the
 * previous request and bumps a version so superseded responses are ignored.
 */
export class LatestRequest {
  private version = 0;
  private controller: AbortController | null = null;

  begin(): { signal: AbortSignal; isCurrent: () => boolean; finish: () => void } {
    const version = ++this.version;
    this.controller?.abort();
    const controller = new AbortController();
    this.controller = controller;
    return {
      signal: controller.signal,
      isCurrent: () => version === this.version,
      finish: () => {
        if (this.controller === controller) this.controller = null;
      },
    };
  }

  invalidate(): void {
    this.version++;
    this.controller?.abort();
  }
}
