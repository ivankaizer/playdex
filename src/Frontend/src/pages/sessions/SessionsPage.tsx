import { useEffect, useRef } from "react";
import { useNavigate } from "react-router";
import { AppLayout } from "@/components/AppLayout";
import { StatsMonthlyHoursChart } from "@/components/StatsMonthlyHoursChart";
import { StatsRecapSection, type RecapPlatformItem, type RecapSummaryItem, type RecapTopGame } from "@/components/StatsRecapSection";
import { apiRequest, errorMessage, isAbortError } from "@/lib/api";
import { formatDateLabel, formatHours } from "@/lib/format";
import { encodeGameId } from "@/lib/gameHash";
import { useAppNavigate } from "@/lib/navigation";
import { useTheme } from "@/lib/theme";
import type {
  ManualActiveSessionResponse,
  ManualMappingSearchResponse,
  ManualPlatformsResponse,
  ManualSessionStartRequest,
  ManualSessionStartResponse,
  ManualSessionStopResponse,
} from "@/models/manual";
import type { AccountSessionsResponse, AccountSessionUpdateResponse } from "@/models/sessions";
import type { AccountStatsRangeResponse } from "@/models/stats";
import { ChangeInput, BoundSelect } from "./components/FormControls";
import { LiveSessionControl } from "./components/LiveSessionControl";
import { SessionActions } from "./components/SessionActions";
import { SessionEditModal } from "./components/SessionEditModal";
import {
  ActiveManualSessionRequest,
  Limit,
  PlatformsRequest,
  SessionsRequestVersions,
  applyDebouncedTitle,
  applyPreset,
  beginDelete,
  beginEditSave,
  beginListLoad,
  buildListRequest,
  buildStatsRequest,
  cancelEdit,
  completeActiveManualSession,
  completeDelete,
  completeEditSave,
  completeListLoad,
  completePlatforms,
  completeStatsLoad,
  createSessionsState,
  currentPage,
  emptySessionEditState,
  end,
  endDelete,
  failDelete,
  failEditSave,
  failListLoad,
  failStatsLoad,
  getRowViews,
  pageNumbers,
  sessionsDeleteRequest,
  setEditEndedAt,
  setEditStartedAt,
  setFrom,
  setOffset,
  setPlatform,
  setTitle,
  setTo,
  sortBy,
  start,
  togglePlatform,
  tryBeginStatsLoad,
  tryBuildEditSubmission,
  tryOpenEdit,
} from "./sessionsState";
import { createLinkedController, useLatestState } from "./support";

const Presets: ReadonlyArray<{ key: string; label: string }> = [
  { key: "today", label: "Today" },
  { key: "yesterday", label: "Yesterday" },
  { key: "this-week", label: "This week" },
  { key: "last-week", label: "Last week" },
  { key: "this-month", label: "This month" },
  { key: "this-year", label: "This year" },
  { key: "last-year", label: "Last year" },
];
const LightPieColors = ["#059669", "#0e7490", "#7c3aed", "#d97706"];
const DarkPieColors = ["#34d399", "#22d3ee", "#a78bfa", "#f59e0b"];

const formatDate = (date: string) => formatDateLabel(new Date(`${date}T00:00:00Z`));

export function SessionsPage() {
  const [state, getState, updateState] = useLatestState(() => createSessionsState(new Date()));
  const [editState, getEditState, updateEditState] = useLatestState(() => emptySessionEditState);
  const [ui, , updateUi] = useLatestState(() => ({ isStoppingManualSession: false, manualActionError: "" }));
  const { theme } = useTheme();
  const navigate = useNavigate();
  const appNavigate = useAppNavigate();

  const lifetime = useRef(new AbortController());
  const requestVersions = useRef(new SessionsRequestVersions());
  const titleDebounce = useRef<ReturnType<typeof setTimeout> | null>(null);
  const listRequest = useRef<AbortController | null>(null);
  const statsRequest = useRef<AbortController | null>(null);
  const activeManualSessionRequest = useRef<AbortController | null>(null);
  const listRequestVersion = useRef(0);
  const stopping = useRef(false);

  const setManualActionError = (manualActionError: string) => updateUi((current) => ({ ...current, manualActionError }));
  const setStopping = (value: boolean) => {
    stopping.current = value;
    updateUi((current) => ({ ...current, isStoppingManualSession: value }));
  };

  const loadPlatforms = async () => {
    const signal = lifetime.current.signal;
    try {
      const response = await apiRequest<ManualPlatformsResponse>(PlatformsRequest, { signal });
      updateState((current) => completePlatforms(current, response));
    } catch (exception) {
      if (isAbortError(exception) && signal.aborted) return;
      setManualActionError(errorMessage(exception));
    }
  };

  const loadActiveManualSession = async () => {
    const version = requestVersions.current.beginActiveManualSessionRequest();
    activeManualSessionRequest.current?.abort();
    const request = createLinkedController(lifetime.current.signal);
    activeManualSessionRequest.current = request;
    try {
      const response = await apiRequest<ManualActiveSessionResponse>(ActiveManualSessionRequest, { signal: request.signal });
      if (requestVersions.current.isCurrentActiveManualSessionRequest(version) && !request.signal.aborted) {
        updateState((current) => completeActiveManualSession(current, response));
      }
    } catch (exception) {
      if (isAbortError(exception) && request.signal.aborted) return;
      if (requestVersions.current.isCurrentActiveManualSessionRequest(version)) {
        setManualActionError(errorMessage(exception));
      }
    } finally {
      if (activeManualSessionRequest.current === request) activeManualSessionRequest.current = null;
    }
  };

  const loadList = async () => {
    listRequest.current?.abort();
    const request = createLinkedController(lifetime.current.signal);
    listRequest.current = request;
    const version = ++listRequestVersion.current;
    updateState(beginListLoad);
    try {
      const response = await apiRequest<AccountSessionsResponse>(buildListRequest(getState()), { signal: request.signal });
      if (version === listRequestVersion.current && !request.signal.aborted) {
        updateState((current) => completeListLoad(current, response));
      }
    } catch (exception) {
      if (isAbortError(exception) && request.signal.aborted) return;
      if (version === listRequestVersion.current) {
        updateState((current) => failListLoad(current, errorMessage(exception)));
      }
    } finally {
      if (listRequest.current === request) listRequest.current = null;
    }
  };

  const loadStats = async () => {
    const version = requestVersions.current.beginStatsRequest();
    statsRequest.current?.abort();
    const begin = tryBeginStatsLoad(getState());
    updateState(() => begin.state);
    if (!begin.started) return;

    const path = buildStatsRequest(getState()) ?? "";
    const request = createLinkedController(lifetime.current.signal);
    statsRequest.current = request;
    try {
      const response = await apiRequest<AccountStatsRangeResponse>(path, { signal: request.signal });
      if (requestVersions.current.isCurrentStatsRequest(version) && !request.signal.aborted) {
        updateState((current) => completeStatsLoad(current, response));
      }
    } catch (exception) {
      if (isAbortError(exception) && request.signal.aborted) return;
      if (requestVersions.current.isCurrentStatsRequest(version)) {
        updateState((current) => failStatsLoad(current, errorMessage(exception)));
      }
    } finally {
      if (statsRequest.current === request) statsRequest.current = null;
    }
  };

  const refreshAll = () => Promise.all([loadList(), loadStats(), loadActiveManualSession()]);

  useEffect(() => {
    if (lifetime.current.signal.aborted) lifetime.current = new AbortController();
    const controller = lifetime.current;
    void Promise.all([refreshAll(), loadPlatforms()]);
    return () => {
      listRequestVersion.current++;
      requestVersions.current.beginStatsRequest();
      requestVersions.current.beginActiveManualSessionRequest();
      controller.abort();
      if (titleDebounce.current !== null) clearTimeout(titleDebounce.current);
      titleDebounce.current = null;
      listRequest.current?.abort();
      statsRequest.current?.abort();
      activeManualSessionRequest.current?.abort();
    };
  }, []);

  const handleTitleChanged = (value: string) => {
    const version = updateState((current) => setTitle(current, value)).titleVersion;
    if (titleDebounce.current !== null) clearTimeout(titleDebounce.current);
    titleDebounce.current = setTimeout(() => {
      titleDebounce.current = null;
      const applied = applyDebouncedTitle(getState(), version);
      if (applied === null) return;
      updateState(() => applied);
      void loadList();
    }, 300);
  };

  const selectPreset = (preset: string) => {
    updateState((current) => applyPreset(current, preset, new Date()));
    void refreshAll();
  };

  const changeFrom = (value: string) => {
    updateState((current) => setFrom(current, value));
    void refreshAll();
  };

  const changeTo = (value: string) => {
    updateState((current) => setTo(current, value));
    void refreshAll();
  };

  const changePlatform = (value: string) => {
    updateState((current) => setPlatform(current, value));
    void refreshAll();
  };

  const togglePlatformSelection = (platform: string) => {
    updateState((current) => togglePlatform(current, platform));
    void refreshAll();
  };

  const sort = (key: string) => {
    updateState((current) => sortBy(current, key));
    void loadList();
  };

  const setPage = (page: number) => {
    updateState((current) => setOffset(current, (page - 1) * Limit));
    void loadList();
  };

  const previousPage = () => {
    updateState((current) => setOffset(current, current.offset - Limit));
    void loadList();
  };

  const nextPage = () => {
    updateState((current) => setOffset(current, current.offset + Limit));
    void loadList();
  };

  const removeSession = async (sessionId: number) => {
    const request = sessionsDeleteRequest(sessionId);
    if (!window.confirm(request.confirmation)) return;

    const signal = lifetime.current.signal;
    updateState((current) => beginDelete(current, sessionId));
    try {
      await apiRequest<unknown>(request.path, { method: request.method, signal });
      updateState(completeDelete);
      await refreshAll();
    } catch (exception) {
      if (!(isAbortError(exception) && signal.aborted)) {
        updateState((current) => failDelete(current, errorMessage(exception)));
      }
    } finally {
      updateState((current) => endDelete(current, sessionId));
    }
  };

  const openSessionEditor = (sessionId: number) => {
    const session = getState().rows.find((row) => row.sessionId === sessionId);
    if (!session) return;
    const opened = tryOpenEdit(getEditState(), session);
    if (opened !== null) updateEditState(() => opened);
  };

  const cancelSessionEdit = () => updateEditState(cancelEdit);

  const openSessionCreator = () => {
    setManualActionError("");
    navigate("/sessions/create");
  };

  const stopManualSession = async () => {
    if (stopping.current) return;
    setStopping(true);
    setManualActionError("");
    const signal = lifetime.current.signal;
    try {
      await apiRequest<ManualSessionStopResponse>("/manual/sessions/stop", { method: "POST", signal });
      await refreshAll();
    } catch (exception) {
      if (!(isAbortError(exception) && signal.aborted)) setManualActionError(errorMessage(exception));
    } finally {
      setStopping(false);
    }
  };

  const searchLiveGames = (path: string, signal: AbortSignal) => apiRequest<ManualMappingSearchResponse>(path, { signal });

  const startManualSession = async (request: ManualSessionStartRequest) => {
    if (stopping.current) return;
    setStopping(true);
    setManualActionError("");
    const signal = lifetime.current.signal;
    try {
      await apiRequest<ManualSessionStartResponse>("/manual/sessions/start", { method: "POST", body: request, signal });
      await refreshAll();
    } catch (exception) {
      if (!(isAbortError(exception) && signal.aborted)) setManualActionError(errorMessage(exception));
    } finally {
      setStopping(false);
    }
  };

  const saveSessionEdit = async () => {
    const { state: built, submission } = tryBuildEditSubmission(getEditState());
    updateEditState(() => built);
    if (submission === null) return;
    updateEditState(beginEditSave);
    const signal = lifetime.current.signal;
    try {
      await apiRequest<AccountSessionUpdateResponse>(submission.path, { method: submission.method, body: submission.body, signal });
      updateEditState(completeEditSave);
      await refreshAll();
    } catch (exception) {
      if (!(isAbortError(exception) && signal.aborted)) {
        updateEditState((current) => failEditSave(current, errorMessage(exception)));
      }
    }
  };

  const navigateToGame = (gameId: string) => appNavigate(`/games/${encodeGameId(gameId)}`);

  const openRecap = () =>
    appNavigate(state.from.length > 0 && state.to.length > 0 ? `/recap?from=${state.from}&to=${state.to}` : "/recap");

  const sortIconPath = (key: string) =>
    state.sortKey === key
      ? state.sortDirection === "asc"
        ? "M10 4l4 4H6l4-4z"
        : "M10 16l-4-4h8l-4 4z"
      : "M6 8l4-4 4 4H6zM14 12l-4 4-4-4h8z";
  const sortIconClass = (key: string) => `h-3 w-3 ${state.sortKey === key ? "text-emerald-700 dark:text-emerald-200" : ""}`;

  const rowViews = getRowViews(state, new Date());
  const hasSessionFilters =
    state.title.trim().length > 0 || state.platform.length > 0 || state.from.length > 0 || state.to.length > 0;
  const pieColors = theme === "dark" ? DarkPieColors : LightPieColors;
  const stats = state.stats;
  const statsSummary: RecapSummaryItem[] = [
    { label: "Hours played", value: formatHours(stats?.totalSeconds ?? 0, 1) },
    { label: "Sessions", value: String(stats?.sessionCount ?? 0) },
    { label: "Top title", value: stats?.topGameTitle ?? "—" },
  ];
  const statsPlatforms: RecapPlatformItem[] = (() => {
    if (stats === null) return [];
    const total = Math.max(
      stats.platforms.reduce((sum, item) => sum + item.totalSeconds, 0),
      1,
    );
    return stats.platforms.map((item) => ({
      key: item.platformKey,
      label: item.platformName,
      value: Math.floor((item.totalSeconds / total) * 100 + 0.5),
      hours: formatHours(item.totalSeconds, 1),
    }));
  })();
  const statsTopGames: RecapTopGame[] =
    stats?.topGames.map((game) => ({ gameId: game.gameId, title: game.title, hours: formatHours(game.totalSeconds, 1) })) ?? [];
  const statsMonths = stats?.months ?? [];
  const statsRangeLabel = (() => {
    if (state.from.length === 0 && state.to.length === 0) return "All time";
    if (state.from.length > 0 && state.to.length > 0) return `${formatDate(state.from)} to ${formatDate(state.to)}`;
    return state.from.length > 0 ? `From ${formatDate(state.from)}` : `Until ${formatDate(state.to)}`;
  })();
  const emptyMessage = hasSessionFilters ? "No sessions match your filters." : "No sessions yet. Sync a provider to populate this list.";
  const page = currentPage(state);

  const ignoredBadge = (
    <span
      className="inline-flex h-5 w-5 items-center justify-center text-slate-500 dark:text-slate-400"
      title="Ignored from stats"
      aria-label="Ignored from stats"
    >
      <svg aria-hidden="true" viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="1.5">
        <circle cx="12" cy="12" r="8"></circle>
        <path strokeLinecap="round" d="M8 8l8 8"></path>
      </svg>
    </span>
  );

  return (
    <AppLayout
      title="Sessions"
      overlay={
        editState.session !== null ? (
          <SessionEditModal
            gameTitle={editState.session.game}
            startedAt={editState.startedAt}
            endedAt={editState.endedAt}
            isSaving={editState.isSaving}
            error={editState.error}
            onStartedAtChanged={(value) => updateEditState((current) => setEditStartedAt(current, value))}
            onEndedAtChanged={(value) => updateEditState((current) => setEditEndedAt(current, value))}
            onSave={() => void saveSessionEdit()}
            onCancel={cancelSessionEdit}
          />
        ) : null
      }
    >
      <section
        data-session-heading-row
        className="mt-10 rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6"
      >
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">Sessions log</p>
            <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">Every session tracked</h2>
          </div>
          <button
            type="button"
            data-session-action="extended"
            onClick={openSessionCreator}
            className="inline-flex items-center gap-2 rounded-full border border-slate-300 bg-white px-4 py-2 text-xs font-semibold uppercase tracking-[0.18em] text-slate-700 shadow-sm transition hover:border-emerald-400 hover:text-emerald-800 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-200 dark:hover:text-emerald-200"
          >
            <svg aria-hidden="true" viewBox="0 0 20 20" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="1.7">
              <path d="M4 4.75h12M4 10h12M4 15.25h7" strokeLinecap="round"></path>
            </svg>
            Session Creator
          </button>
        </div>
      </section>

      <LiveSessionControl
        platforms={state.platforms}
        activeSession={state.activeManualSession}
        busy={ui.isStoppingManualSession}
        error={ui.manualActionError}
        searchGames={searchLiveGames}
        onStart={startManualSession}
        onStop={stopManualSession}
      />

      <section
        data-session-history-row
        className="mt-5 rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6"
      >
        <div className="mt-6 flex flex-wrap gap-2">
          {Presets.map((preset) => (
            <button
              key={preset.key}
              type="button"
              onClick={() => selectPreset(preset.key)}
              className={
                preset.key === state.preset
                  ? "rounded-full border px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.3em] transition border-emerald-300 bg-emerald-400 text-slate-900"
                  : "rounded-full border px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.3em] transition border-slate-300 dark:border-slate-700 text-slate-700 dark:text-slate-200 hover:border-emerald-300/70"
              }
            >
              {preset.label}
            </button>
          ))}
        </div>

        <div className="mt-6 grid gap-3 md:grid-cols-2 xl:grid-cols-[1.2fr_0.8fr_0.6fr_0.6fr]">
          <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
            Title
            <input
              type="text"
              placeholder="Search by game title"
              value={state.title}
              onChange={(event) => handleTitleChanged(event.currentTarget.value)}
              className="mt-2 w-full rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
            />
          </label>
          <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
            Platform
            <BoundSelect
              value={state.platform}
              onValueChange={changePlatform}
              className="mt-2 w-full rounded-xl border border-slate-300 bg-white/90 px-3 py-2 text-sm text-slate-900 outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30 dark:border-slate-700 dark:bg-slate-950/80 dark:text-white"
            >
              <option value="">All platforms</option>
              {statsPlatforms.map((platform) => (
                <option key={platform.key} value={platform.key}>
                  {platform.label}
                </option>
              ))}
            </BoundSelect>
          </label>
          <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
            From
            <ChangeInput
              type="date"
              value={state.from}
              onValueChange={changeFrom}
              className="mt-2 w-full rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
            />
          </label>
          <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
            To
            <ChangeInput
              type="date"
              value={state.to}
              onValueChange={changeTo}
              className="mt-2 w-full rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
            />
          </label>
        </div>

        {state.listError.length > 0 && <p className="mt-4 text-xs text-rose-700 dark:text-rose-300">{state.listError}</p>}
        {state.statsError.length > 0 && <p className="mt-4 text-xs text-rose-700 dark:text-rose-300">{state.statsError}</p>}

        <div className="mt-6">
          <StatsRecapSection
            title="Sessions recap"
            subtitle={statsRangeLabel}
            summary={statsSummary}
            platforms={statsPlatforms}
            topGames={statsTopGames}
            colors={pieColors}
            selectedPlatform={state.platform}
            onPlatformSelect={togglePlatformSelection}
            onGameSelect={navigateToGame}
            onOpenRecap={openRecap}
          />
          {state.isStatsLoading && <p className="mt-3 text-xs text-slate-500 dark:text-slate-400">Loading stats...</p>}
        </div>

        <div className="mt-6 rounded-3xl border border-slate-200 bg-white/80 p-5 dark:border-slate-800 dark:bg-slate-900/60 sm:p-6">
          <div className="flex flex-wrap items-baseline justify-between gap-3">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">Monthly trend</p>
              <h3 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">Hours per month</h3>
            </div>
            {state.platform.length > 0 && (
              <span className="rounded-full bg-emerald-100 px-3 py-1 text-xs font-semibold text-emerald-900 dark:bg-emerald-400/10 dark:text-emerald-100">
                {statsPlatforms.find((item) => item.key === state.platform)?.label}
              </span>
            )}
          </div>
          <div className="mt-6">
            {state.isStatsLoading ? (
              <div className="h-40 animate-pulse rounded-2xl bg-slate-200/70 dark:bg-slate-800/60"></div>
            ) : (
              <StatsMonthlyHoursChart months={statsMonths} />
            )}
          </div>
        </div>

        <div className="mt-6 space-y-3 md:hidden">
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/60 px-4 py-3 text-[11px] uppercase tracking-[0.25em] text-slate-500 dark:text-slate-400">
            <span>Sort</span>
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={() => sort("date")}
                className="inline-flex items-center gap-2 rounded-full border border-slate-300 dark:border-slate-700 px-3 py-1 font-semibold transition hover:border-emerald-400/60 hover:text-emerald-700 dark:hover:text-emerald-100"
              >
                Date
                <svg aria-hidden="true" viewBox="0 0 20 20" className={sortIconClass("date")} fill="currentColor">
                  <path d={sortIconPath("date")}></path>
                </svg>
              </button>
              <button
                type="button"
                onClick={() => sort("duration")}
                className="inline-flex items-center gap-2 rounded-full border border-slate-300 dark:border-slate-700 px-3 py-1 font-semibold transition hover:border-emerald-400/60 hover:text-emerald-700 dark:hover:text-emerald-100"
              >
                Duration
                <svg aria-hidden="true" viewBox="0 0 20 20" className={sortIconClass("duration")} fill="currentColor">
                  <path d={sortIconPath("duration")}></path>
                </svg>
              </button>
            </div>
          </div>

          <div className="grid gap-3">
            {state.isListLoading ? (
              [1, 2, 3, 4].map((row) => (
                <div key={row} className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/60 p-4">
                  <div className="flex items-start gap-3">
                    <div className="h-14 w-10 rounded-xl bg-slate-200/70 dark:bg-slate-800/60"></div>
                    <div className="flex-1 space-y-2">
                      <div className="h-3 w-32 rounded-full bg-slate-200/70 dark:bg-slate-800/60"></div>
                      <div className="h-3 w-20 rounded-full bg-slate-200/50 dark:bg-slate-800/50"></div>
                      <div className="h-3 w-16 rounded-full bg-slate-200/50 dark:bg-slate-800/50"></div>
                    </div>
                    <div className="h-3 w-12 rounded-full bg-slate-200/50 dark:bg-slate-800/50"></div>
                  </div>
                  <div className="mt-3 h-8 w-20 rounded-full bg-slate-200/50 dark:bg-slate-800/50"></div>
                </div>
              ))
            ) : rowViews.length === 0 ? (
              <div className="rounded-2xl border border-dashed border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4 text-sm text-slate-500 dark:text-slate-400">
                {emptyMessage}
              </div>
            ) : (
              rowViews.map((session) => (
                <div
                  key={session.sessionId}
                  className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/60 p-4"
                >
                  <div className="flex items-start gap-3">
                    {session.imageUrl !== null ? (
                      <img
                        src={session.imageUrl}
                        alt={`${session.title} capsule art`}
                        className="h-14 w-10 rounded-xl border border-slate-200 dark:border-slate-800 object-cover"
                      />
                    ) : (
                      <div className="h-14 w-10 rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-200/70 dark:bg-slate-800/60"></div>
                    )}
                    <div className="flex-1 space-y-1">
                      <button
                        type="button"
                        onClick={() => navigateToGame(session.gameId)}
                        className="text-left font-semibold text-slate-900 dark:text-white transition hover:text-emerald-700 dark:hover:text-emerald-200"
                      >
                        {session.title}
                      </button>
                      <p className="text-xs text-slate-500 dark:text-slate-400">{session.platform}</p>
                      <p className="text-xs text-slate-500 dark:text-slate-400">{session.date}</p>
                    </div>
                    <div className="flex items-center gap-2">
                      {session.ignored && ignoredBadge}
                      <span className="text-sm font-semibold text-emerald-700 dark:text-emerald-200">{session.duration}</span>
                    </div>
                  </div>
                  <div className="mt-3 flex items-center justify-between">
                    <span className="text-[11px] uppercase tracking-[0.3em] text-slate-500">{session.platform}</span>
                    <SessionActions
                      canEdit={session.canEdit}
                      disabled={state.deletingSessionIds.has(session.sessionId)}
                      onEdit={() => openSessionEditor(session.sessionId)}
                      onRemove={() => void removeSession(session.sessionId)}
                    />
                  </div>
                </div>
              ))
            )}
          </div>
        </div>

        <div className="mt-6 hidden overflow-hidden rounded-2xl border border-slate-200 dark:border-slate-800 md:block">
          <table className="w-full text-left text-sm">
            <thead className="bg-white dark:bg-slate-950/70 text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
              <tr>
                <th className="px-4 py-3 text-left">Title</th>
                <th className="px-4 py-3 text-left">Platform</th>
                <th className="px-4 py-3 text-left">
                  <button
                    type="button"
                    onClick={() => sort("date")}
                    className="inline-flex items-center gap-2 text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400 transition hover:text-slate-700 dark:hover:text-slate-200"
                  >
                    Date
                    <svg aria-hidden="true" viewBox="0 0 20 20" className={sortIconClass("date")} fill="currentColor">
                      <path d={sortIconPath("date")}></path>
                    </svg>
                  </button>
                </th>
                <th className="px-4 py-3 text-right">
                  <button
                    type="button"
                    onClick={() => sort("duration")}
                    className="inline-flex items-center gap-2 text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400 transition hover:text-slate-700 dark:hover:text-slate-200"
                  >
                    Duration
                    <svg aria-hidden="true" viewBox="0 0 20 20" className={sortIconClass("duration")} fill="currentColor">
                      <path d={sortIconPath("duration")}></path>
                    </svg>
                  </button>
                </th>
                <th className="px-4 py-3 text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-200 dark:divide-slate-800 bg-white/80 dark:bg-slate-900/60">
              {state.isListLoading ? (
                [1, 2, 3, 4].map((row) => (
                  <tr key={row}>
                    <td className="px-4 py-4">
                      <div className="flex items-center gap-3">
                        <div className="h-12 w-9 rounded-lg bg-slate-200/70 dark:bg-slate-800/60"></div>
                        <div className="h-3 w-28 rounded-full bg-slate-200/70 dark:bg-slate-800/60"></div>
                      </div>
                    </td>
                    <td className="px-4 py-4">
                      <div className="h-3 w-16 rounded-full bg-slate-200/50 dark:bg-slate-800/50"></div>
                    </td>
                    <td className="px-4 py-4">
                      <div className="h-3 w-12 rounded-full bg-slate-200/50 dark:bg-slate-800/50"></div>
                    </td>
                    <td className="px-4 py-4">
                      <div className="ml-auto h-3 w-12 rounded-full bg-slate-200/50 dark:bg-slate-800/50"></div>
                    </td>
                    <td className="px-4 py-4">
                      <div className="ml-auto h-3 w-16 rounded-full bg-slate-200/40 dark:bg-slate-800/40"></div>
                    </td>
                  </tr>
                ))
              ) : rowViews.length === 0 ? (
                <tr>
                  <td colSpan={5} className="px-4 py-10 text-center text-sm text-slate-500 dark:text-slate-400">
                    {emptyMessage}
                  </td>
                </tr>
              ) : (
                rowViews.map((session) => (
                  <tr key={session.sessionId} className="transition hover:bg-slate-100 dark:hover:bg-slate-900/70">
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-3">
                        {session.imageUrl !== null ? (
                          <img
                            src={session.imageUrl}
                            alt={`${session.title} capsule art`}
                            className="h-12 w-9 rounded-lg border border-slate-200 dark:border-slate-800 object-cover"
                          />
                        ) : (
                          <div className="h-12 w-9 rounded-lg border border-slate-200 dark:border-slate-800 bg-slate-200/70 dark:bg-slate-800/60"></div>
                        )}
                        <button
                          type="button"
                          onClick={() => navigateToGame(session.gameId)}
                          className="text-left font-semibold text-slate-900 dark:text-white transition hover:text-emerald-700 dark:hover:text-emerald-200"
                        >
                          {session.title}
                        </button>
                      </div>
                    </td>
                    <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{session.platform}</td>
                    <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{session.date}</td>
                    <td className="px-4 py-3 text-right text-emerald-700 dark:text-emerald-200">
                      <div className="inline-flex items-center justify-end gap-2">
                        {session.ignored && ignoredBadge}
                        <span>{session.duration}</span>
                      </div>
                    </td>
                    <td className="px-4 py-3 text-right">
                      <SessionActions
                        canEdit={session.canEdit}
                        disabled={state.deletingSessionIds.has(session.sessionId)}
                        onEdit={() => openSessionEditor(session.sessionId)}
                        onRemove={() => void removeSession(session.sessionId)}
                      />
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        <div className="mt-6 flex flex-wrap items-center justify-between gap-4 text-xs text-slate-600 dark:text-slate-300">
          <p>
            Showing {start(state)}-{end(state)} of {state.total} sessions
          </p>
          <div className="flex items-center gap-2">
            <button
              type="button"
              disabled={state.offset === 0 || state.isListLoading}
              onClick={previousPage}
              className="rounded-full border border-slate-300 dark:border-slate-700 px-3 py-2 font-semibold uppercase tracking-[0.3em] text-slate-700 dark:text-slate-200"
            >
              Prev
            </button>
            {pageNumbers(state).map((pageNumber) => (
              <button
                key={pageNumber}
                type="button"
                onClick={() => setPage(pageNumber)}
                className={
                  pageNumber === page
                    ? "flex h-9 w-9 items-center justify-center rounded-full text-xs font-semibold leading-none bg-emerald-400 text-slate-900"
                    : "flex h-9 w-9 items-center justify-center rounded-full text-xs font-semibold leading-none border border-slate-300 dark:border-slate-700 text-slate-700 dark:text-slate-200"
                }
              >
                {pageNumber}
              </button>
            ))}
            <button
              type="button"
              disabled={state.offset + Limit >= state.total || state.isListLoading}
              onClick={nextPage}
              className="rounded-full border border-slate-300 dark:border-slate-700 px-3 py-2 font-semibold uppercase tracking-[0.3em] text-slate-700 dark:text-slate-200"
            >
              Next
            </button>
          </div>
        </div>
      </section>
    </AppLayout>
  );
}
