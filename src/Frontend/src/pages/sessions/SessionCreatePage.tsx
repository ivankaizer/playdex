import { useEffect, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router";
import { AppLayout } from "@/components/AppLayout";
import { Breadcrumbs } from "@/components/Breadcrumbs";
import { apiRequest, errorMessage, isAbortError } from "@/lib/api";
import type { DraftBatchDto, DraftCapabilities } from "@/models/drafts";
import type {
  ManualEntryCreateResponse,
  ManualMappingSearchResponse,
  ManualPlatform,
  ManualPlatformsResponse,
  ManualSessionStartResponse,
} from "@/models/manual";
import { DraftWorkspace, type DraftWorkspaceView } from "./components/DraftWorkspace";
import { SessionCreateModal } from "./components/SessionCreateModal";
import {
  cancelSearch,
  closeSessionCreate,
  completeSave,
  completeSearch,
  failSave,
  failSearch,
  formatMinutes,
  openSessionCreate,
  tryBeginSave,
  tryBeginSearch,
  tryBuildSubmission,
  type SessionCreateMode,
} from "./sessionCreateState";
import { createLinkedController, useLatestState } from "./support";

const ToastMilliseconds = 2600;

function modeFromQuery(search: string): SessionCreateMode {
  return search.toLowerCase().includes("mode=live") ? "live" : "historical";
}

export function SessionCreatePage() {
  const { search: query } = useLocation();
  const [state, getState, updateState] = useLatestState(() => openSessionCreate(modeFromQuery(query), new Date()));
  const [platforms, setPlatforms] = useState<readonly ManualPlatform[]>([]);
  const [draftCapabilities, setDraftCapabilities] = useState<DraftCapabilities | null>(null);
  const [draftsOpen, setDraftsOpen] = useState(false);
  const [draftsView, setDraftsView] = useState<DraftWorkspaceView>("compose");
  const [batchCount, setBatchCount] = useState(0);
  const [toast, setToast] = useState<string | null>(null);
  const toastTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const navigate = useNavigate();

  const lifetime = useRef(new AbortController());
  const searchRequest = useRef<AbortController | null>(null);
  const saveRequest = useRef<AbortController | null>(null);
  const searchVersion = useRef(0);

  const showToast = (message: string) => {
    clearTimeout(toastTimer.current);
    setToast(message);
    toastTimer.current = setTimeout(() => setToast(null), ToastMilliseconds);
  };

  const invalidateSearch = () => {
    ++searchVersion.current;
    searchRequest.current?.abort();
  };

  useEffect(() => {
    if (lifetime.current.signal.aborted) lifetime.current = new AbortController();
    const controller = lifetime.current;
    const signal = controller.signal;

    const loadPlatforms = async () => {
      try {
        const response = await apiRequest<ManualPlatformsResponse>("/manual/platforms", { signal });
        setPlatforms(response.platforms);
      } catch {
        if (!signal.aborted) setPlatforms([]);
      }
    };
    const loadCapabilities = async () => {
      try {
        setDraftCapabilities(await apiRequest<DraftCapabilities>("/manual/draft-capabilities", { signal }));
      } catch {
        if (!signal.aborted) setDraftCapabilities(null);
      }
    };
    const loadBatchCount = async () => {
      try {
        const batches = await apiRequest<DraftBatchDto[]>("/manual/draft-batches", { signal });
        setBatchCount(batches.length);
      } catch {
        // The badge is optional; the AI workspace reports its own load errors.
      }
    };
    void Promise.all([loadPlatforms(), loadCapabilities(), loadBatchCount()]);

    return () => {
      controller.abort();
      clearTimeout(toastTimer.current);
      invalidateSearch();
      saveRequest.current?.abort();
    };
  }, []);

  const openDrafts = () => {
    updateState(closeSessionCreate);
    invalidateSearch();
    setDraftsOpen(true);
  };

  const closeDrafts = () => {
    setDraftsOpen(false);
    setDraftsView("compose");
    updateState(() => openSessionCreate("historical", new Date()));
  };

  const toggleWorkspace = () => {
    if (draftsOpen) closeDrafts();
    else openDrafts();
  };

  const queryChanged = () => {
    invalidateSearch();
    updateState(cancelSearch);
  };

  const search = async () => {
    const begin = tryBeginSearch(getState());
    updateState(() => begin.state);
    if (begin.path === null) return;
    invalidateSearch();
    const version = searchVersion.current;
    const request = createLinkedController(lifetime.current.signal);
    searchRequest.current = request;
    try {
      const response = await apiRequest<ManualMappingSearchResponse>(begin.path, { signal: request.signal });
      if (version === searchVersion.current && !request.signal.aborted) {
        updateState((current) => completeSearch(current, response));
      }
    } catch (exception) {
      if (isAbortError(exception) && request.signal.aborted) return;
      if (version === searchVersion.current) updateState((current) => failSearch(current, errorMessage(exception)));
    } finally {
      if (searchRequest.current === request) searchRequest.current = null;
    }
  };

  const save = async () => {
    if (getState().isSaving) return;
    const built = tryBuildSubmission(getState());
    updateState(() => built.state);
    if (built.submission === null) return;
    const began = tryBeginSave(getState());
    if (began === null) return;
    updateState(() => began);

    const submission = built.submission;
    saveRequest.current?.abort();
    const request = createLinkedController(lifetime.current.signal);
    saveRequest.current = request;
    try {
      if (submission.kind === "entry") {
        await apiRequest<ManualEntryCreateResponse>(submission.path, {
          method: submission.method,
          body: submission.body,
          signal: request.signal,
        });
      } else {
        await apiRequest<ManualSessionStartResponse>(submission.path, {
          method: submission.method,
          body: submission.body,
          signal: request.signal,
        });
      }
      if (submission.kind === "entry") {
        // Past sessions: confirm with a toast and reset the form so the next one can be logged.
        showToast(`${submission.body.title ?? "Session"} · ${formatMinutes(submission.body.durationMinutes)} saved to your history`);
        invalidateSearch();
        updateState(() => openSessionCreate("historical", new Date()));
      } else {
        updateState(completeSave);
        navigate("/sessions");
      }
    } catch (exception) {
      if (isAbortError(exception) && request.signal.aborted) return;
      updateState((current) => failSave(current, errorMessage(exception)));
    } finally {
      if (saveRequest.current === request) saveRequest.current = null;
    }
  };

  const backToSessions = () => navigate("/sessions");

  const crumb = !draftsOpen ? "Log a session" : draftsView === "review" ? "Review batch" : "AI batch";
  const modeTab = (open: boolean, label: string, badge?: number) => (
    <button
      type="button"
      data-mode-tab={open ? "ai" : "manual"}
      aria-pressed={draftsOpen === open}
      onClick={() => {
        if (draftsOpen !== open) toggleWorkspace();
      }}
      className="pdx-seg-btn flex h-[38px] items-center gap-2 px-[18px]"
    >
      {label}
      {badge !== undefined && badge > 0 && (
        <span className="pdx-num grid h-5 min-w-5 place-items-center rounded-full bg-brand px-1.5 text-[11px] text-white">
          <span className="sr-only">(</span>
          {badge}
          <span className="sr-only"> saved)</span>
        </span>
      )}
    </button>
  );

  return (
    <AppLayout
      title="Log a session"
      overlay={
        toast !== null && (
          <div role="status" className="pdx-toast">
            <span aria-hidden="true" className="text-[#6fe3b5]">
              ✓
            </span>{" "}
            {toast}
          </div>
        )
      }
    >
      <section className="flex flex-wrap items-center justify-between gap-4 rounded-[28px] border border-[color:var(--card-border)] bg-[var(--card-bg)] py-2.5 pl-7 pr-2.5 shadow-card sm:rounded-full">
        <Breadcrumbs parentLabel="Sessions" currentLabel={crumb} onParent={backToSessions} />
        <div role="group" aria-label="Entry mode" className="pdx-seg">
          {modeTab(false, "Manual entry")}
          {modeTab(true, "AI batch", batchCount)}
        </div>
      </section>

      {draftsOpen ? (
        <DraftWorkspace
          embedded
          capabilities={draftCapabilities}
          platforms={platforms}
          onClose={closeDrafts}
          onAccepted={() => undefined}
          onToast={showToast}
          onViewChange={setDraftsView}
          onBatchesChange={setBatchCount}
          onGoToSessions={backToSessions}
        />
      ) : (
        <SessionCreateModal
          embedded
          state={state}
          onStateChange={updateState}
          aiAvailable={draftCapabilities?.available === true}
          platforms={platforms}
          onUseAi={openDrafts}
          onQueryChanged={queryChanged}
          onSearch={() => void search()}
          onSave={() => void save()}
          onClose={backToSessions}
        />
      )}
    </AppLayout>
  );
}
