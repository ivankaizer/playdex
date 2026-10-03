import { useEffect, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router";
import { AppLayout } from "@/components/AppLayout";
import { Breadcrumbs } from "@/components/Breadcrumbs";
import { apiRequest, errorMessage, isAbortError } from "@/lib/api";
import type { DraftCapabilities } from "@/models/drafts";
import type {
  ManualEntryCreateResponse,
  ManualMappingSearchResponse,
  ManualPlatform,
  ManualPlatformsResponse,
  ManualSessionStartResponse,
} from "@/models/manual";
import { DraftWorkspace } from "./components/DraftWorkspace";
import { SessionCreateModal } from "./components/SessionCreateModal";
import {
  cancelSearch,
  closeSessionCreate,
  completeSave,
  completeSearch,
  failSave,
  failSearch,
  openSessionCreate,
  tryBeginSave,
  tryBeginSearch,
  tryBuildSubmission,
  type SessionCreateMode,
} from "./sessionCreateState";
import { createLinkedController, useLatestState } from "./support";

function modeFromQuery(search: string): SessionCreateMode {
  return search.toLowerCase().includes("mode=live") ? "live" : "historical";
}

export function SessionCreatePage() {
  const { search: query } = useLocation();
  const [state, getState, updateState] = useLatestState(() => openSessionCreate(modeFromQuery(query), new Date()));
  const [platforms, setPlatforms] = useState<readonly ManualPlatform[]>([]);
  const [draftCapabilities, setDraftCapabilities] = useState<DraftCapabilities | null>(null);
  const [draftsOpen, setDraftsOpen] = useState(false);
  const navigate = useNavigate();

  const lifetime = useRef(new AbortController());
  const searchRequest = useRef<AbortController | null>(null);
  const saveRequest = useRef<AbortController | null>(null);
  const searchVersion = useRef(0);

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
    void Promise.all([loadPlatforms(), loadCapabilities()]);

    return () => {
      controller.abort();
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
      updateState(completeSave);
      navigate("/sessions");
    } catch (exception) {
      if (isAbortError(exception) && request.signal.aborted) return;
      updateState((current) => failSave(current, errorMessage(exception)));
    } finally {
      if (saveRequest.current === request) saveRequest.current = null;
    }
  };

  const backToSessions = () => navigate("/sessions");

  return (
    <AppLayout title="Session Creator">
      <section className="mt-10 space-y-6">
        <header className="flex flex-wrap items-center justify-between gap-4 rounded-2xl border border-slate-200 bg-white/80 p-4 dark:border-slate-800 dark:bg-slate-900/60">
          <Breadcrumbs parentLabel="Sessions" currentLabel="Session Creator" onParent={backToSessions} />
          <button
            type="button"
            data-workspace-toggle
            onClick={toggleWorkspace}
            className="rounded-full border border-slate-300 px-4 py-2 text-xs font-semibold uppercase tracking-[0.18em] text-slate-700 transition hover:border-emerald-400 hover:text-emerald-700 dark:border-slate-700 dark:text-slate-200 dark:hover:border-emerald-400 dark:hover:text-emerald-300"
          >
            {draftsOpen ? "Manual session" : "AI batches"}
          </button>
        </header>

        {draftsOpen ? (
          <DraftWorkspace embedded capabilities={draftCapabilities} platforms={platforms} onClose={closeDrafts} onAccepted={() => undefined} />
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
      </section>
    </AppLayout>
  );
}
