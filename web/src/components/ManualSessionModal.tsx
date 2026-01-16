import type { ManualInput } from "../types";
import { formatManualInputLabel } from "../utils/formatters";

type ManualSessionModalProps = {
  isOpen: boolean;
  manualInputs: ManualInput[];
  manualInputsError: string;
  manualInputSelection: string;
  manualInputText: string;
  manualStartError: string;
  isManualInputsLoading: boolean;
  isManualStartLoading: boolean;
  onClose: () => void;
  onSelectInput: (value: string) => void;
  onInputTextChange: (value: string) => void;
  onStart: () => void;
};

export const ManualSessionModal = ({
  isOpen,
  manualInputs,
  manualInputsError,
  manualInputSelection,
  manualInputText,
  manualStartError,
  isManualInputsLoading,
  isManualStartLoading,
  onClose,
  onSelectInput,
  onInputTextChange,
  onStart,
}: ManualSessionModalProps) => {
  if (!isOpen) {
    return null;
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/80 px-4 py-8">
      <div className="w-full max-w-lg rounded-3xl border border-slate-800 bg-slate-900/90 p-6 shadow-[0_30px_80px_rgba(15,23,42,0.7)]">
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
              Manual tracking
            </p>
            <h2 className="mt-2 text-xl font-semibold text-white">
              Start a session
            </h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="rounded-full border border-slate-700 px-3 py-1 text-xs font-semibold uppercase tracking-[0.3em] text-slate-200"
          >
            Close
          </button>
        </div>

        <div className="mt-6 grid gap-4">
          <label className="text-xs font-medium text-slate-300">
            Pick from history
            <select
              value={manualInputSelection}
              onChange={(event) => onSelectInput(event.target.value)}
              disabled={isManualInputsLoading}
              className="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950/80 px-3 py-2 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
            >
              <option value="">Choose a recent title</option>
              {manualInputs.map((input) => (
                <option key={input.id} value={input.input}>
                  {formatManualInputLabel(input)}
                </option>
              ))}
            </select>
          </label>

          <label className="text-xs font-medium text-slate-300">
            Or enter a new title
            <input
              type="text"
              placeholder="Mario Kart"
              value={manualInputText}
              onChange={(event) => onInputTextChange(event.target.value)}
              className="mt-2 w-full rounded-xl border border-slate-700 bg-slate-950/80 px-3 py-2 text-sm text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
            />
          </label>

          {manualInputsError ? (
            <p className="text-xs text-rose-300">{manualInputsError}</p>
          ) : null}
          {manualStartError ? (
            <p className="text-xs text-rose-300">{manualStartError}</p>
          ) : null}
        </div>

        <div className="mt-6 flex flex-wrap items-center justify-end gap-3">
          <button
            type="button"
            onClick={onClose}
            className="rounded-full border border-slate-700 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-200"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={onStart}
            disabled={
              isManualStartLoading ||
              isManualInputsLoading ||
              !manualInputText.trim()
            }
            className="rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-emerald-300/60"
          >
            {isManualStartLoading ? "Starting..." : "Start session"}
          </button>
        </div>
      </div>
    </div>
  );
};
