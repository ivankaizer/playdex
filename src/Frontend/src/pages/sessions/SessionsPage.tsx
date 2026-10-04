import { useEffect, useRef } from "react";
import { useNavigate } from "react-router";
import { AppLayout } from "@/components/AppLayout";
import {
  BarChart,
  CardHeading,
  Cover,
  Donut,
  Dot,
  ErrorNote,
  LegendRow,
  LoadingNote,
  ShareBar,
  StatTile,
  type BarDatum,
  type DonutSegment,
} from "@/components/ui";
import { apiRequest, errorMessage, isAbortError } from "@/lib/api";
import { cx } from "@/lib/cx";
import { formatMonthLabel, monthLabelsSpanYears } from "@/lib/dates";
import { formatHours } from "@/lib/format";
import { encodeGameId } from "@/lib/gameHash";
import { useAppNavigate } from "@/lib/navigation";
import { platformColor } from "@/lib/platforms";
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
  groupRowViews,
  pageNumbers,
  rangeLabel,
  selectCustomRange,
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
  totalPages,
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

const fieldLabel = "flex min-w-0 flex-col gap-1.5 text-xs font-medium text-muted";
const autoFit = (min: number) => ({ gridTemplateColumns: `repeat(auto-fit, minmax(min(${min}px, 100%), 1fr))` });

function IgnoredBadge() {
  return (
    <span className="inline-flex h-5 w-5 items-center justify-center text-faint" title="Ignored from stats" aria-label="Ignored from stats">
      <svg aria-hidden="true" viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="1.5">
        <circle cx="12" cy="12" r="8"></circle>
        <path strokeLinecap="round" d="M8 8l8 8"></path>
      </svg>
    </span>
  );
}

export function SessionsPage() {
  const [state, getState, updateState] = useLatestState(() => createSessionsState(new Date()));
  const [editState, getEditState, updateEditState] = useLatestState(() => emptySessionEditState);
  const [ui, , updateUi] = useLatestState(() => ({ isStoppingManualSession: false, manualActionError: "", showLive: false }));
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

  const selectCustom = () => updateState(selectCustomRange);

  const toggleLiveControl = () => updateUi((current) => ({ ...current, showLive: !current.showLive }));

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

  const now = new Date();
  const rowViews = getRowViews(state, now);
  const groups = groupRowViews(state, rowViews, now);
  const groupedByDay = state.sortKey === "date";
  const isCustom = state.preset === "custom";
  const showLive = ui.showLive || state.activeManualSession !== null || ui.manualActionError.length > 0;
  const hasRange = state.from.length > 0 || state.to.length > 0;

  const stats = state.stats;
  const platformItems = (() => {
    if (stats === null) return [];
    const total = Math.max(
      stats.platforms.reduce((sum, item) => sum + item.totalSeconds, 0),
      1,
    );
    return stats.platforms.map((item) => ({
      key: item.platformKey,
      label: item.platformName,
      pct: Math.floor((item.totalSeconds / total) * 100 + 0.5),
      hours: formatHours(item.totalSeconds, 1),
      seconds: item.totalSeconds,
      color: platformColor(item.platformKey, item.platformName),
    }));
  })();
  const donutSegments: DonutSegment[] = platformItems.map((item) => ({
    key: item.key,
    label: item.label,
    value: item.seconds,
    color: item.color,
  }));
  const topGames = stats?.topGames ?? [];
  const topGameMax = Math.max(topGames[0]?.totalSeconds ?? 0, 1);
  const months = stats?.months ?? [];
  const spansYears = monthLabelsSpanYears(months);
  const monthPeak = Math.max(...months.map((month) => month.totalSeconds), 0);
  const monthBars: BarDatum[] = months.map((month) => {
    const label = formatMonthLabel(month, spansYears);
    return {
      key: `${month.year}-${month.month}`,
      value: month.totalSeconds,
      label,
      title: `${label}: ${formatHours(month.totalSeconds, 1)}`,
      highlight: monthPeak > 0 && month.totalSeconds === monthPeak,
    };
  });
  const hasRecapData = stats !== null && stats.sessionCount > 0 && (platformItems.length > 0 || topGames.length > 0);
  const selectedPlatformLabel =
    state.platform.length === 0 ? "" : (platformItems.find((item) => item.key === state.platform)?.label ?? state.platform);
  const kpiValue = (value: string) => (stats === null && state.isStatsLoading ? "…" : value);

  const page = currentPage(state);
  const pages = totalPages(state);
  const sortDirectionLabel = state.sortDirection === "asc" ? "ascending" : "descending";
  const sortGlyph = state.sortDirection === "asc" ? "↑" : "↓";
  const flatGroupLabel =
    state.sortKey === "duration" ? (state.sortDirection === "desc" ? "LONGEST FIRST" : "SHORTEST FIRST") : "SESSIONS";

  const logPastSessionButton = (className: string) => (
    <button type="button" data-session-action="extended" onClick={openSessionCreator} className={className}>
      + Log past session
    </button>
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
      <section data-session-heading-row className="pdx-card flex flex-col gap-6 sm:!py-8">
        <CardHeading
          kicker="Sessions log"
          title="Every session tracked"
          actions={
            <>
              {logPastSessionButton("pdx-btn-outline !h-11 !text-ink")}
              {state.activeManualSession === null && (
                <button
                  type="button"
                  aria-expanded={showLive ? "true" : "false"}
                  aria-controls="live-session-control"
                  onClick={toggleLiveControl}
                  className="pdx-btn-primary !h-11 !px-[22px]"
                >
                  ▶ Start live session
                </button>
              )}
            </>
          }
        />

        {showLive && (
          <div id="live-session-control" className="[&>section]:mt-0">
            <LiveSessionControl
              platforms={state.platforms}
              activeSession={state.activeManualSession}
              busy={ui.isStoppingManualSession}
              error={ui.manualActionError}
              searchGames={searchLiveGames}
              onStart={startManualSession}
              onStop={stopManualSession}
            />
          </div>
        )}

        <div role="group" aria-label="Date range" className="flex flex-wrap gap-1.5">
          {Presets.map((preset) => (
            <button
              key={preset.key}
              type="button"
              aria-pressed={preset.key === state.preset ? "true" : "false"}
              onClick={() => selectPreset(preset.key)}
              className="pdx-chip font-semibold"
            >
              {preset.label}
            </button>
          ))}
          <button type="button" aria-pressed={isCustom ? "true" : "false"} onClick={selectCustom} className="pdx-chip font-semibold">
            Custom
          </button>
        </div>

        <div className="flex flex-wrap items-end gap-3">
          <label className={cx(fieldLabel, "flex-[2_1_240px]")}>
            Title
            <input
              type="text"
              placeholder="Search by game title"
              value={state.title}
              onChange={(event) => handleTitleChanged(event.currentTarget.value)}
              className="pdx-input !h-11"
            />
          </label>
          <label className={cx(fieldLabel, "flex-[1_1_170px]")}>
            Platform
            <BoundSelect value={state.platform} onValueChange={changePlatform} className="pdx-input !h-11 !pl-4">
              <option value="">All platforms</option>
              {platformItems.map((platform) => (
                <option key={platform.key} value={platform.key}>
                  {platform.label}
                </option>
              ))}
            </BoundSelect>
          </label>
          {isCustom && (
            <>
              <label className={cx(fieldLabel, "flex-[1_1_150px]")}>
                From
                <ChangeInput type="date" value={state.from} onValueChange={changeFrom} className="pdx-input !h-11 !px-4" />
              </label>
              <label className={cx(fieldLabel, "flex-[1_1_150px]")}>
                To
                <ChangeInput type="date" value={state.to} onValueChange={changeTo} className="pdx-input !h-11 !px-4" />
              </label>
            </>
          )}
        </div>
      </section>

      <section data-session-recap className="pdx-card flex flex-col gap-8">
        <CardHeading
          kicker="Range recap"
          title={rangeLabel(state)}
          actions={
            <>
              {selectedPlatformLabel.length > 0 && (
                <button
                  type="button"
                  aria-pressed="true"
                  aria-label={`Clear ${selectedPlatformLabel} filter`}
                  onClick={() => changePlatform("")}
                  className="pdx-chip"
                >
                  {selectedPlatformLabel} ×
                </button>
              )}
              <button type="button" onClick={openRecap} className="pdx-btn-outline" title="Open recap">
                Open recap →
              </button>
            </>
          }
        />

        {state.statsError.length > 0 && <ErrorNote>{state.statsError}</ErrorNote>}

        <div className="grid gap-4" style={autoFit(200)}>
          <StatTile label="Hours played" value={kpiValue(formatHours(stats?.totalSeconds ?? 0, 1))} />
          <StatTile label="Sessions" value={kpiValue((stats?.sessionCount ?? 0).toLocaleString("en-US"))} />
          <StatTile label="Top title" value={kpiValue(stats?.topGameTitle ?? "—")} />
        </div>

        {hasRecapData && monthBars.length > 2 && (
          <div className="flex flex-col gap-3.5">
            <p className="pdx-label">Hours per month</p>
            <BarChart data={monthBars} height={150} gap={12} radius={10} minBar={6} ariaLabel="Hours per month" />
          </div>
        )}

        {hasRecapData && (
          <div className="grid gap-4" style={autoFit(320)}>
            <div className="pdx-tile flex flex-col gap-6 !p-7">
              <p className="pdx-label">Platforms</p>
              <div className="flex flex-wrap items-center gap-8">
                <Donut
                  segments={donutSegments}
                  center={
                    <div>
                      <p className="pdx-num text-xl font-semibold">{formatHours(stats?.totalSeconds ?? 0, 1)}</p>
                      <p className="text-xs text-muted">total</p>
                    </div>
                  }
                />
                <div className="flex min-w-0 flex-[1_1_140px] flex-col gap-1">
                  {platformItems.map((item) => (
                    <button
                      key={item.key}
                      type="button"
                      aria-pressed={state.platform === item.key ? "true" : "false"}
                      onClick={() => togglePlatformSelection(item.key)}
                      className={cx(
                        "pdx-row -mx-2 px-2 py-1.5 text-left",
                        state.platform === item.key && "bg-mint",
                        state.platform.length > 0 && state.platform !== item.key && "opacity-55",
                      )}
                    >
                      <LegendRow color={item.color} name={item.label} pct={`${item.pct}%`} detail={item.hours} />
                    </button>
                  ))}
                </div>
              </div>
            </div>

            <div className="pdx-tile flex flex-col gap-[18px] !p-7">
              <p className="pdx-label">Top games</p>
              {topGames.map((game, index) => (
                <button
                  key={`${game.gameId}-${game.title}`}
                  type="button"
                  onClick={() => navigateToGame(game.gameId)}
                  className="group grid grid-cols-[26px_minmax(0,1fr)_auto] items-baseline gap-x-3 gap-y-1.5 text-left"
                >
                  <span className="pdx-num text-[13px] text-faint">{String(index + 1).padStart(2, "0")}</span>
                  <span className="truncate text-[15px] font-medium transition group-hover:text-accent">{game.title}</span>
                  <span className="pdx-num text-sm font-semibold text-accent">{formatHours(game.totalSeconds, 1)}</span>
                  <ShareBar pct={(game.totalSeconds / topGameMax) * 100} className="col-span-2 col-start-2" />
                </button>
              ))}
            </div>
          </div>
        )}

        {state.isStatsLoading && <LoadingNote>Loading stats…</LoadingNote>}
      </section>

      <section data-session-history-row className="pdx-card flex flex-col sm:!pb-8 sm:!pt-7">
        <div className="flex flex-wrap items-center justify-end gap-2">
          <span className="pdx-label mr-1">Sort</span>
          {(["date", "duration"] as const).map((key) => (
            <button
              key={key}
              type="button"
              data-sort={key}
              aria-pressed={state.sortKey === key ? "true" : "false"}
              aria-label={`Sort by ${key}${state.sortKey === key ? `, ${sortDirectionLabel}` : ""}`}
              onClick={() => sort(key)}
              className="pdx-chip !h-8 !px-3.5 !text-xs"
            >
              {key === "date" ? "Date" : "Duration"}
              {state.sortKey === key && <span aria-hidden="true">{sortGlyph}</span>}
            </button>
          ))}
        </div>

        {state.listError.length > 0 && (
          <div className="pt-4">
            <ErrorNote>{state.listError}</ErrorNote>
          </div>
        )}

        {state.isListLoading ? (
          <div className="flex flex-col gap-2 pt-5" aria-busy="true" aria-label="Loading sessions">
            {[1, 2, 3, 4].map((row) => (
              <div key={row} className="flex animate-pulse items-center gap-[18px] py-2.5">
                <div className="h-[52px] w-[52px] rounded-[14px] bg-track"></div>
                <div className="flex-1 space-y-2">
                  <div className="h-3 w-40 max-w-full rounded-full bg-track"></div>
                  <div className="h-3 w-20 rounded-full bg-track"></div>
                </div>
                <div className="h-3 w-12 rounded-full bg-track"></div>
              </div>
            ))}
          </div>
        ) : groups.length === 0 ? (
          <div className="flex flex-col items-start gap-3.5 py-12">
            <p className="text-xl font-semibold">{hasRange ? "No sessions in this range" : "No sessions yet"}</p>
            <p className="text-sm text-muted">
              {hasRange
                ? "Pick another range, or log a session you played offline."
                : "Sync a provider to populate this list, or log a session you played offline."}
            </p>
            {logPastSessionButton("pdx-btn-outline !h-[42px] !text-ink")}
          </div>
        ) : (
          groups.map((group) => (
            <div key={group.key}>
              <div className="flex items-baseline justify-between gap-4 pb-2.5 pt-5">
                <span className="text-[11px] font-semibold tracking-[0.24em] text-accent">
                  {group.label.length > 0 ? group.label : flatGroupLabel}
                </span>
                <span className="pdx-num text-[13px] text-muted">{group.meta}</span>
              </div>
              <ul className="flex flex-col">
                {group.rows.map((session) => (
                  <li
                    key={session.sessionId}
                    className="pdx-row -mx-3.5 grid grid-cols-[52px_minmax(0,1fr)_auto] items-center gap-x-[18px] gap-y-2 !rounded-[20px] px-3.5 py-2.5 sm:grid-cols-[52px_minmax(0,1fr)_90px_auto]"
                  >
                    <Cover
                      title={session.title}
                      src={session.imageUrl}
                      className="h-[52px] w-[52px] rounded-[14px] shadow-cover"
                      initialClassName="text-[17px]"
                    />
                    <div className="min-w-0">
                      <button
                        type="button"
                        onClick={() => navigateToGame(session.gameId)}
                        className="block max-w-full truncate text-left text-[15px] font-medium transition hover:text-accent"
                      >
                        {session.title}
                      </button>
                      <p className="mt-[3px] flex min-w-0 items-center gap-1.5 text-[13px] text-muted">
                        <Dot color={platformColor(session.platformKey, session.platform)} size={7} />
                        <span className="truncate">
                          {session.platform}
                          {!groupedByDay && ` · ${session.date}`}
                        </span>
                      </p>
                    </div>
                    <div className="flex items-center justify-end gap-1.5">
                      {session.ignored && <IgnoredBadge />}
                      <span className="pdx-num text-right text-[15px] font-semibold text-accent">{session.duration}</span>
                    </div>
                    <div className="col-span-2 col-start-2 flex justify-end sm:col-span-1 sm:col-start-auto">
                      <SessionActions
                        canEdit={session.canEdit}
                        disabled={state.deletingSessionIds.has(session.sessionId)}
                        onEdit={() => openSessionEditor(session.sessionId)}
                        onRemove={() => void removeSession(session.sessionId)}
                      />
                    </div>
                  </li>
                ))}
              </ul>
            </div>
          ))
        )}

        {state.total > 0 && (
          <div className="mt-3 flex flex-wrap items-center justify-between gap-4 border-t border-divider pt-6">
            <span className="pdx-num text-[13px] text-muted">
              Showing {start(state)}–{end(state)} of {state.total.toLocaleString("en-US")} sessions
            </span>
            {pages > 1 && (
              <nav aria-label="Pagination" className="flex flex-wrap gap-1.5">
                <button
                  type="button"
                  disabled={state.offset === 0 || state.isListLoading}
                  onClick={previousPage}
                  className="pdx-chip !h-[38px] !px-3.5 font-semibold"
                >
                  ← Prev
                </button>
                {pageNumbers(state).map((pageNumber) => (
                  <button
                    key={pageNumber}
                    type="button"
                    aria-current={pageNumber === page ? "page" : undefined}
                    onClick={() => setPage(pageNumber)}
                    className={cx(
                      "pdx-chip !h-[38px] min-w-[38px] justify-center !px-3.5 font-semibold",
                      pageNumber === page && "!border-brand !bg-brand !text-white",
                    )}
                  >
                    {pageNumber}
                  </button>
                ))}
                <button
                  type="button"
                  disabled={state.offset + Limit >= state.total || state.isListLoading}
                  onClick={nextPage}
                  className="pdx-chip !h-[38px] !px-3.5 font-semibold"
                >
                  Next →
                </button>
              </nav>
            )}
          </div>
        )}
      </section>
    </AppLayout>
  );
}
