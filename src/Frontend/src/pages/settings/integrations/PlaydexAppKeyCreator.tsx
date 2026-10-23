import { useState } from "react";
import { apiRequest, errorMessage } from "@/lib/api";
import { setupUrl } from "@/lib/downloads";

interface PlaydexAppKeyCreatorProps {
  /** Reloads the integrations, so the new Playdex app integration shows with its setup values. */
  onCreated: () => Promise<void>;
}

/** Before a user has a Playdex app key: one button that creates it (POST /key returns the existing key if any). */
export function PlaydexAppKeyCreator({ onCreated }: PlaydexAppKeyCreatorProps) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function create() {
    setBusy(true);
    setError(null);
    try {
      await apiRequest<{ apiKey: string }>("/key", { method: "POST" });
      await onCreated();
    } catch (failure) {
      setError(errorMessage(failure));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div
      data-playdex-app-key
      className="flex flex-wrap items-center gap-4 rounded-tile border border-[var(--tile-border)] bg-tile px-5 py-4"
    >
      <div className="min-w-0 flex-[1_1_240px]">
        <p className="text-sm font-semibold">Set up Playdex for Windows</p>
        <p className="mt-1 text-[13px] text-muted">
          Records the games you play on your PC. Create a key, then enter it in{" "}
          <a href={setupUrl} className="font-semibold text-accent underline underline-offset-2">
            Playdex-Setup.exe
          </a>
          .
        </p>
        {error ? (
          <p role="alert" className="mt-2 text-[13px] text-danger">
            {error}
          </p>
        ) : null}
      </div>
      <button type="button" onClick={() => void create()} disabled={busy} className="pdx-btn-primary !h-10 !px-5 !text-[13px]">
        {busy ? "Creating..." : "Create API key"}
      </button>
    </div>
  );
}
