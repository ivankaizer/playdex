import { useEffect, useId, useRef, useState } from "react";
import { apiBaseUrl } from "@/lib/api";
import { setupUrl } from "@/lib/downloads";
import type { Integrations } from "./useIntegrations";

interface PlaydexAppSetupProps {
  integrations: Integrations;
}

type Copied = "server" | "key" | null;

/**
 * How to set up Playdex for Windows, on the Playdex app's integration: where to get the installer, and the two values
 * its setup asks for, the server (the API this dashboard uses, so it is right on every deployment) and the API key.
 * The server keeps only a hash of the key, so the key shows right after it is created or replaced, and never again.
 */
export function PlaydexAppSetup({ integrations }: PlaydexAppSetupProps) {
  const { issuedAppKey, isAppKeyIssuing, appKeyError } = integrations.state;
  const server = apiBaseUrl();
  const [copied, setCopied] = useState<Copied>(null);
  const [confirmingReplace, setConfirmingReplace] = useState(false);
  const reset = useRef<number | undefined>(undefined);

  useEffect(() => () => window.clearTimeout(reset.current), []);

  async function copy(what: Exclude<Copied, null>, value: string) {
    try {
      await navigator.clipboard.writeText(value);
    } catch {
      return; // The browser refused the clipboard; the value stays selectable.
    }
    setCopied(what);
    window.clearTimeout(reset.current);
    reset.current = window.setTimeout(() => setCopied(null), 2000);
  }

  async function replace() {
    await integrations.issueAppKey();
    setConfirmingReplace(false);
  }

  return (
    <div data-playdex-app-setup className="mx-5 mb-5 rounded-[20px] border border-line bg-surface p-4 max-[600px]:mx-4 max-[600px]:mb-4">
      <p className="text-sm font-semibold">Set up Playdex for Windows</p>
      <p className="mt-1 text-[13px] text-muted">
        The Playdex app records the games you play on your PC. Download and run{" "}
        <a href={setupUrl} className="font-semibold text-accent underline underline-offset-2">
          Playdex-Setup.exe
        </a>{" "}
        and enter the server and your API key when it asks. They are also in the app under Settings → Your Playdex account.
      </p>
      <div className="mt-3 grid gap-3 [grid-template-columns:repeat(auto-fit,minmax(min(260px,100%),1fr))]">
        <CopyField label="Playdex server" value={server} copied={copied === "server"} onCopy={() => void copy("server", server)} />
        {issuedAppKey ? (
          <CopyField label="API key" value={issuedAppKey} hidden copied={copied === "key"} onCopy={() => void copy("key", issuedAppKey)} />
        ) : (
          <div className="pdx-label flex min-w-0 flex-col gap-2">
            API key
            <div className="flex h-11 items-center rounded-full border border-line bg-tile px-[18px] text-sm normal-case tracking-normal text-muted">
              Shown only when it's created
            </div>
          </div>
        )}
      </div>

      {issuedAppKey ? (
        <p data-playdex-app-key-once className="mt-3 text-[13px] text-muted">
          Copy the key now. Playdex doesn't keep it, so it won't show it again.
        </p>
      ) : null}

      {confirmingReplace ? (
        <div className="mt-3 flex flex-wrap items-center gap-2">
          <p className="min-w-0 flex-[1_1_240px] text-[13px] text-muted">
            A new key stops the old one. The Playdex app on each of your PCs stops recording until you enter the new key there.
          </p>
          <button
            type="button"
            onClick={() => void replace()}
            disabled={isAppKeyIssuing}
            className="pdx-btn-danger !h-[34px] !px-3.5 !text-xs"
          >
            {isAppKeyIssuing ? "Replacing..." : "Replace key"}
          </button>
          <button
            type="button"
            onClick={() => setConfirmingReplace(false)}
            disabled={isAppKeyIssuing}
            className="pdx-btn-outline !h-[34px] !px-3.5 !text-xs"
          >
            Cancel
          </button>
        </div>
      ) : (
        <button type="button" onClick={() => setConfirmingReplace(true)} className="pdx-btn-outline mt-3 !h-[34px] !px-3.5 !text-xs">
          {issuedAppKey ? "Replace key again" : "Lost the key? Replace it"}
        </button>
      )}

      {appKeyError ? (
        <p role="alert" className="mt-2 text-[13px] text-danger">
          {appKeyError}
        </p>
      ) : null}
    </div>
  );
}

interface CopyFieldProps {
  label: string;
  value: string;
  hidden?: boolean;
  copied: boolean;
  onCopy: () => void;
}

function CopyField({ label, value, hidden = false, copied, onCopy }: CopyFieldProps) {
  const id = useId();
  return (
    <div className="pdx-label flex min-w-0 flex-col gap-2">
      <label htmlFor={id}>{label}</label>
      <span className="flex min-w-0 gap-2">
        <input
          id={id}
          type={hidden ? "password" : "text"}
          value={value}
          readOnly
          aria-readonly="true"
          autoComplete="off"
          className="pdx-input !h-11 min-w-0 flex-1 !bg-tile font-mono tracking-normal text-ink-2"
        />
        <button
          type="button"
          onClick={onCopy}
          aria-label={`Copy ${label.toLowerCase()}`}
          className="pdx-btn-outline !h-11 shrink-0 !px-4 !text-[13px]"
        >
          {copied ? "Copied" : "Copy"}
        </button>
      </span>
    </div>
  );
}
