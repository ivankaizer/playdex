import { setupUrl } from "@/lib/downloads";
import type { Integrations } from "./useIntegrations";

interface PlaydexAppKeyCreatorProps {
  integrations: Integrations;
}

/**
 * Before a user has a Playdex app key: one button that creates it. The new Playdex app integration then shows the key
 * with its setup values, once.
 */
export function PlaydexAppKeyCreator({ integrations }: PlaydexAppKeyCreatorProps) {
  const { isAppKeyIssuing: busy, appKeyError: error } = integrations.state;

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
      <button
        type="button"
        onClick={() => void integrations.issueAppKey()}
        disabled={busy}
        className="pdx-btn-primary !h-10 !px-5 !text-[13px]"
      >
        {busy ? "Creating..." : "Create API key"}
      </button>
    </div>
  );
}
