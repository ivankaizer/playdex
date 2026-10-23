import { useEffect, useId, useRef, useState } from "react";
import { apiBaseUrl } from "@/lib/api";
import { setupUrl } from "@/lib/downloads";

interface PlaydexAppSetupProps {
  apiKey: string;
}

type Copied = "server" | "key" | null;

/**
 * How to set up Playdex for Windows, on the Playdex app's integration: where to get the installer, and the two values
 * its setup asks for, the server (the API this dashboard uses, so it is right on every deployment) and the API key.
 */
export function PlaydexAppSetup({ apiKey }: PlaydexAppSetupProps) {
  const server = apiBaseUrl();
  const [copied, setCopied] = useState<Copied>(null);
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

  return (
    <div data-playdex-app-setup className="mx-5 mb-5 rounded-[20px] border border-line bg-surface p-4 max-[600px]:mx-4 max-[600px]:mb-4">
      <p className="text-sm font-semibold">Set up Playdex for Windows</p>
      <p className="mt-1 text-[13px] text-muted">
        The Playdex app records the games you play on your PC. Download and run{" "}
        <a href={setupUrl} className="font-semibold text-accent underline underline-offset-2">
          Playdex-Setup.exe
        </a>{" "}
        and enter these two values when it asks. They are also in the app under Settings → Your Playdex account.
      </p>
      <div className="mt-3 grid gap-3 [grid-template-columns:repeat(auto-fit,minmax(min(260px,100%),1fr))]">
        <CopyField label="Playdex server" value={server} copied={copied === "server"} onCopy={() => void copy("server", server)} />
        <CopyField label="API key" value={apiKey} hidden copied={copied === "key"} onCopy={() => void copy("key", apiKey)} />
      </div>
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
