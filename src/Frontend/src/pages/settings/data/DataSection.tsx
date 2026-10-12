import { useState, type FormEvent } from "react";
import { apiBlob, apiRequest, errorMessage } from "@/lib/api";
import { authSession } from "@/lib/auth";
import { formatUtcDateInput } from "@/lib/format";
import { logout } from "@/lib/navigation";
import { SectionHeading } from "../shared/SectionHeading";

type ExportStatus = "idle" | "working" | "done" | "error";

/** Saves a blob through a temporary object URL. */
function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.append(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

/** Settings card: download a copy of your data, or permanently delete the account. */
export function DataSection() {
  const [exportStatus, setExportStatus] = useState<ExportStatus>("idle");
  const [exportError, setExportError] = useState("");
  const [isConfirming, setIsConfirming] = useState(false);
  const [password, setPassword] = useState("");
  const [isDeleting, setIsDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState("");

  const downloadExport = async () => {
    setExportStatus("working");
    setExportError("");
    try {
      const blob = await apiBlob("/me/export", { onAuthFailure: logout });
      saveBlob(blob, `playdex-export-${formatUtcDateInput(new Date())}.json`);
      setExportStatus("done");
    } catch (error) {
      setExportStatus("error");
      setExportError(errorMessage(error));
    }
  };

  const cancelDelete = () => {
    setIsConfirming(false);
    setPassword("");
    setDeleteError("");
  };

  const deleteAccount = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (password.length === 0 || isDeleting) return;
    setIsDeleting(true);
    setDeleteError("");
    try {
      await apiRequest<void>("/me", { method: "DELETE", body: { password }, onAuthFailure: logout });
      authSession.logout();
      window.location.assign("/");
    } catch (error) {
      setIsDeleting(false);
      setDeleteError(errorMessage(error));
    }
  };

  return (
    <section id="your-data" aria-labelledby="your-data-title" className="pdx-card flex scroll-mt-6 flex-col gap-6">
      <SectionHeading
        kicker="Your data"
        title={<span id="your-data-title">Export or delete your data</span>}
        description="Everything Playdex stores about you, in your hands."
      />

      <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(320px,100%),1fr))]">
        <div className="pdx-tile flex flex-col gap-4">
          <div>
            <p className="pdx-label">Export</p>
            <p className="mt-2 text-[15px] font-semibold">Download a copy</p>
            <p className="mt-1 text-sm leading-relaxed text-muted">
              A JSON file with your profile, settings, connected services (without their keys), every session, ratings,
              game connections, AI drafts and AI batch inputs from the last 30 days.
            </p>
          </div>
          <div className="mt-auto flex flex-wrap items-center gap-3">
            <button
              type="button"
              onClick={downloadExport}
              disabled={exportStatus === "working"}
              className="pdx-btn-outline"
            >
              {exportStatus === "working" ? "Preparing…" : "Download my data"}
            </button>
            {exportStatus === "done" && <span className="text-sm text-accent">✓ Download started</span>}
            {exportStatus === "error" && (
              <span role="alert" className="text-sm text-danger">
                {exportError}
              </span>
            )}
          </div>
        </div>

        <div className="pdx-tile flex flex-col gap-4 !border-danger-line">
          <div>
            <p className="pdx-label !text-danger">Danger zone</p>
            <p className="mt-2 text-[15px] font-semibold">Delete your account</p>
            <p className="mt-1 text-sm leading-relaxed text-muted">
              Permanently removes your account, sessions, connected services and their keys, settings, ratings, drafts and
              AI history. This can't be undone.
            </p>
          </div>
          {isConfirming ? (
            <form onSubmit={deleteAccount} aria-label="Delete account" className="mt-auto flex flex-col gap-3">
              <label htmlFor="delete-account-password" className="text-sm font-medium">
                Enter your password to confirm
              </label>
              <input
                id="delete-account-password"
                type="password"
                autoComplete="current-password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                disabled={isDeleting}
                className="pdx-input"
                autoFocus
              />
              {deleteError && (
                <p role="alert" className="m-0 text-sm text-danger">
                  {deleteError}
                </p>
              )}
              <div className="flex flex-wrap gap-3">
                <button type="submit" disabled={password.length === 0 || isDeleting} className="pdx-btn-danger">
                  {isDeleting ? "Deleting…" : "Permanently delete account"}
                </button>
                <button type="button" onClick={cancelDelete} disabled={isDeleting} className="pdx-btn-outline">
                  Cancel
                </button>
              </div>
            </form>
          ) : (
            <div className="mt-auto">
              <button type="button" onClick={() => setIsConfirming(true)} className="pdx-btn-danger">
                Delete account…
              </button>
            </div>
          )}
        </div>
      </div>
    </section>
  );
}
