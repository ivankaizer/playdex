import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { useLocation } from "react-router";
import { AppLayout } from "@/components/AppLayout";
import { BarChart, CardHeading, Cover, Dot, ErrorNote, LoadingNote, Segmented, ShareBar, StackedBar } from "@/components/ui";
import { apiRequest, errorMessage } from "@/lib/api";
import { formatDuration, formatHours } from "@/lib/format";
import { encodeGameId } from "@/lib/gameHash";
import { logout, useAppNavigate } from "@/lib/navigation";
import { platformColor } from "@/lib/platforms";
import type { RecapResponse, RecapYearsResponse } from "./recapModels";
import {
  applyLocationQuery,
  beginLoad,
  beginYearsLoad,
  buildCustomNavigation,
  completeLoad,
  completeYears,
  failLoad,
  failYears,
  initialRecapState,
  recapTabs,
  recapYearsPath,
  runInitialCustomFlow,
  selectTab,
  setCustomFrom,
  setCustomTo,
  takeDefaultRequest,
  type RecapState,
  type RecapStep,
} from "./recapState";
import { formatAverage, formatRecapDate, formatRecapMonthLabel, rangeLabel, roundPercent, type LabeledValue } from "./recapView";

const WeekdayLabels = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
const WeekdayNames = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

const pad2 = (value: number) => String(value).padStart(2, "0");
const count = (value: number) => value.toLocaleString("en-US");

export function RecapPage() {
  const location = useLocation();
  const navigate = useAppNavigate();
  const [state, setState] = useState<RecapState>(initialRecapState);
  const stateRef = useRef(state);
  const lifetimeRef = useRef<AbortController | null>(null);
  const recapRequestRef = useRef<AbortController | null>(null);
  const recapVersionRef = useRef(0);
  const initializedRef = useRef(false);

  const apply = useCallback((transition: (current: RecapState) => RecapState) => {
    stateRef.current = transition(stateRef.current);
    setState(stateRef.current);
  }, []);

  const step = useCallback(<R,>(transition: (current: RecapState) => RecapStep<R>): R => {
    const [next, result] = transition(stateRef.current);
    stateRef.current = next;
    setState(next);
    return result;
  }, []);

  const loadYears = useCallback(
    async (lifetime: AbortController) => {
      apply(beginYearsLoad);
      try {
        const response = await apiRequest<RecapYearsResponse>(recapYearsPath, {
          onAuthFailure: logout,
          signal: lifetime.signal,
        });
        if (!lifetime.signal.aborted) apply((current) => completeYears(current, response));
      } catch (error) {
        if (!lifetime.signal.aborted) apply((current) => failYears(current, errorMessage(error)));
      }
    },
    [apply],
  );

  const loadRecap = useCallback(
    async (lifetime: AbortController, path: string) => {
      if (lifetime.signal.aborted) return;
      recapRequestRef.current?.abort();
      const request = new AbortController();
      const abortRequest = () => request.abort();
      lifetime.signal.addEventListener("abort", abortRequest, { once: true });
      recapRequestRef.current = request;
      const version = ++recapVersionRef.current;
      apply(beginLoad);

      try {
        const response = await apiRequest<RecapResponse>(path, { onAuthFailure: logout, signal: request.signal });
        if (version === recapVersionRef.current) apply((current) => completeLoad(current, response));
      } catch (error) {
        if (request.signal.aborted) return;
        if (version === recapVersionRef.current) apply((current) => failLoad(current, errorMessage(error)));
      } finally {
        lifetime.signal.removeEventListener("abort", abortRequest);
        if (recapRequestRef.current === request) recapRequestRef.current = null;
      }
    },
    [apply],
  );

  const takeDefault = useCallback(
    (lifetime: AbortController) => (lifetime.signal.aborted ? null : step(takeDefaultRequest)),
    [step],
  );

  // Component lifetime (re-created on StrictMode remount, which also resets the page state).
  useEffect(() => {
    const lifetime = new AbortController();
    lifetimeRef.current = lifetime;
    initializedRef.current = false;
    return () => {
      lifetime.abort();
      recapRequestRef.current?.abort();
      recapRequestRef.current = null;
      stateRef.current = initialRecapState;
    };
  }, []);

  // Equivalent of OnParametersSetAsync: re-runs when the `from`/`to` query values change.
  const queryKey = useMemo(() => {
    const params = new URLSearchParams(location.search);
    return JSON.stringify([params.get("from"), params.get("to")]);
  }, [location.search]);

  useEffect(() => {
    const lifetime = lifetimeRef.current;
    if (lifetime === null) return;
    const firstRender = !initializedRef.current;
    initializedRef.current = true;
    const request = step((current) => applyLocationQuery(current, location.search));

    const run = async () => {
      if (firstRender) {
        if (request !== null) {
          await runInitialCustomFlow(
            request,
            () => loadYears(lifetime),
            () => takeDefault(lifetime),
            (path) => loadRecap(lifetime, path),
          );
          return;
        }

        await loadYears(lifetime);
        const defaultRequest = takeDefault(lifetime);
        if (defaultRequest !== null) await loadRecap(lifetime, defaultRequest);
        return;
      }

      if (request !== null) {
        await loadRecap(lifetime, request);
        return;
      }

      const fallbackRequest = takeDefault(lifetime);
      if (fallbackRequest !== null) await loadRecap(lifetime, fallbackRequest);
    };

    void run();
  }, [queryKey, step, loadYears, loadRecap, takeDefault]);

  const handleSelectTab = (key: string) => {
    const request = step((current) => selectTab(current, key));
    const lifetime = lifetimeRef.current;
    if (request !== null && lifetime !== null) void loadRecap(lifetime, request);
  };

  const applyCustom = () => {
    const target = buildCustomNavigation(stateRef.current);
    if (target === null) return;
    navigate(target);
  };

  const navigateToGame = (gameId: string) => navigate(`/games/${encodeGameId(gameId)}`);

  const tabs = recapTabs(state);
  const activeLabel = tabs.find((tab) => tab.key === state.activeTab)?.label ?? "All time";
  const recap = state.recap;

  return (
    <AppLayout title="Recap">
      <section
        className="pdx-card flex flex-col gap-8"
        style={{ background: "var(--hero-bg)" }}
        aria-label={`${activeLabel} recap`}
      >
        <div className="flex flex-wrap items-center justify-between gap-4">
          <p className="pdx-kicker">
            {activeLabel} recap
            {state.activeTab === "custom" && recap?.range.from && recap.range.to && (
              <span className="text-muted"> · {rangeLabel(recap)}</span>
            )}
          </p>
          <Segmented
            ariaLabel="Recap period"
            options={tabs.map((tab) => ({ value: tab.key, label: tab.label }))}
            value={state.activeTab}
            onChange={handleSelectTab}
          />
        </div>

        {state.activeTab === "custom" && (
          <div className="grid items-end gap-3 [grid-template-columns:repeat(auto-fit,minmax(min(180px,100%),1fr))]">
            <label className="pdx-label flex flex-col gap-2">
              From
              <input
                type="date"
                value={state.customFrom}
                onChange={(event) => apply((current) => setCustomFrom(current, event.target.value))}
                className="pdx-input normal-case tracking-normal"
              />
            </label>
            <label className="pdx-label flex flex-col gap-2">
              To
              <input
                type="date"
                value={state.customTo}
                onChange={(event) => apply((current) => setCustomTo(current, event.target.value))}
                className="pdx-input normal-case tracking-normal"
              />
            </label>
            <button type="button" onClick={applyCustom} className="pdx-btn-primary">
              Apply
            </button>
          </div>
        )}

        {state.error.length > 0 && <ErrorNote>{state.error}</ErrorNote>}
        {state.isLoading && <LoadingNote>Loading recap...</LoadingNote>}

        {recap !== null && (
          <div className="flex flex-wrap items-end justify-between gap-8">
            <div className="min-w-0">
              <p className="text-[15px] text-ink-2">You played</p>
              <p className="pdx-num mt-1.5 break-words text-[56px] font-semibold leading-none tracking-[-0.04em] sm:text-[80px]">
                {formatHours(recap.totalSeconds, 1)}
              </p>
              <p className="mt-3 text-base text-ink-2">
                across {count(recap.totalSessions)} {recap.totalSessions === 1 ? "session" : "sessions"} and{" "}
                {count(recap.uniqueGames)} {recap.uniqueGames === 1 ? "game" : "games"}
              </p>
            </div>
            <div className="flex flex-wrap gap-3">
              {heroFacts(recap).map((fact) => (
                <div
                  key={fact.label}
                  className="min-w-[150px] max-w-full rounded-[20px] border border-[color:var(--card-border)] bg-surface/75 px-5 py-4"
                >
                  <p className="pdx-label">{fact.label}</p>
                  <p className="pdx-num mt-1.5 truncate text-xl font-semibold">{fact.value}</p>
                </div>
              ))}
            </div>
          </div>
        )}
      </section>

      {recap !== null && (
        <>
          <section className="pdx-card flex flex-col gap-6">
            <CardHeading kicker="Most played" title="Top 5 games" />
            {recap.topGamesByTime.length === 0 ? (
              <p className="pdx-dashed">No game data available yet.</p>
            ) : (
              <div className="grid gap-5 [grid-template-columns:repeat(auto-fit,minmax(min(160px,100%),1fr))]">
                {recap.topGamesByTime.map((game, index) => (
                  <button
                    key={index}
                    type="button"
                    onClick={() => navigateToGame(game.gameId)}
                    className="group flex min-w-0 flex-col gap-3 text-left"
                  >
                    <div className="relative w-full">
                      <Cover
                        title={game.title}
                        src={game.capsuleUrl}
                        className="aspect-[2/3] w-full rounded-[22px] shadow-cover"
                        style={{ background: "linear-gradient(160deg,#2b3540,rgb(var(--ink-3)))" }}
                        initialClassName="text-[40px]"
                      />
                      <span className="pdx-num absolute left-2.5 top-2.5 grid h-8 min-w-8 place-items-center rounded-full bg-white/90 px-2.5 text-[13px] font-bold text-[#0b3d2c]">
                        {index + 1}
                      </span>
                    </div>
                    <div className="min-w-0">
                      <p className="line-clamp-2 text-[15px] font-medium leading-[1.3] group-hover:text-accent">{game.title}</p>
                      <p className="pdx-num mt-0.5 text-sm font-semibold text-accent">{formatHours(game.totalSeconds, 1)}</p>
                    </div>
                  </button>
                ))}
              </div>
            )}
          </section>

          <RhythmCard recap={recap} />

          <div className="grid gap-7 [grid-template-columns:repeat(auto-fit,minmax(min(340px,100%),1fr))]">
            <ConsistencyCard recap={recap} />
            <SessionsCard recap={recap} />
          </div>

          <PlatformMixCard recap={recap} />

          <LibraryCard recap={recap} onOpenGame={navigateToGame} />
        </>
      )}
    </AppLayout>
  );
}

function heroFacts(recap: RecapResponse): LabeledValue[] {
  const facts: LabeledValue[] = [{ label: "Top title", value: recap.topGameByTime?.title ?? "—" }];
  facts.push({
    label: "Most active month",
    value: recap.mostActiveMonth ? formatRecapMonthLabel(recap.mostActiveMonth.month) : "—",
  });
  facts.push({ label: "Avg. session", value: formatDuration(recap.averageSessionSeconds, "full") });
  return facts;
}

function SideNote({ children }: { children: ReactNode }) {
  return <span className="text-[13px] text-muted">{children}</span>;
}

function RhythmCard({ recap }: { recap: RecapResponse }) {
  const hourMax = Math.max(0, ...recap.sessionsByHour.map((item) => item.sessionCount));
  const hours = recap.sessionsByHour.map((item) => ({
    key: String(item.hour),
    value: item.sessionCount,
    label: item.hour % 3 === 0 ? pad2(item.hour) : "",
    title: `${pad2(item.hour)}:00 · ${count(item.sessionCount)} sessions`,
    highlight: hourMax > 0 && item.sessionCount === hourMax,
  }));

  const weekdayCounts = WeekdayLabels.map(
    (_, index) => recap.sessionsByWeekday.find((item) => item.weekday === index)?.sessionCount ?? 0,
  );
  const weekdayMax = Math.max(0, ...weekdayCounts);
  const busiestIndex = weekdayMax > 0 ? weekdayCounts.indexOf(weekdayMax) : -1;
  const weekdays = weekdayCounts.map((value, index) => ({
    key: WeekdayLabels[index],
    value,
    label: WeekdayLabels[index],
    title: `${WeekdayLabels[index]} · ${count(value)} sessions`,
    highlight: index === busiestIndex,
  }));

  const splitTotal = recap.weekendSeconds + recap.weekdaySeconds;
  const weekendPercent = splitTotal > 0 ? (recap.weekendSeconds / splitTotal) * 100 : 0;
  const weekdayPercent = splitTotal > 0 ? (recap.weekdaySeconds / splitTotal) * 100 : 0;

  return (
    <section className="pdx-card flex flex-col gap-7">
      <CardHeading kicker="Rhythm" title="When you play" actions={<SideNote>{rangeLabel(recap)}</SideNote>} />
      <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(320px,100%),1fr))]">
        <div className="pdx-tile flex flex-col gap-3.5 !p-7">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <span className="pdx-label">By hour</span>
            <span className="pdx-num text-[13px] text-ink-2">
              Peak{" "}
              {recap.peakHour ? (
                <>
                  <b className="font-semibold">{pad2(recap.peakHour.hour)}:00</b> · {count(recap.peakHour.sessionCount)} sessions
                </>
              ) : (
                "—"
              )}
            </span>
          </div>
          {hours.length === 0 ? (
            <p className="pdx-dashed">No sessions yet.</p>
          ) : (
            <BarChart data={hours} height={130} gap={4} radius={6} ariaLabel="Sessions by hour" />
          )}
        </div>

        <div className="pdx-tile flex flex-col gap-3.5 !p-7">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <span className="pdx-label">By weekday</span>
            <span className="text-[13px] text-ink-2">
              Busiest <b className="font-semibold">{busiestIndex >= 0 ? WeekdayNames[busiestIndex] : "—"}</b>
            </span>
          </div>
          <BarChart data={weekdays} height={90} gap={10} radius={10} ariaLabel="Sessions by weekday" />
          <div className="mt-2 flex h-2.5 gap-[3px] overflow-hidden rounded-full bg-track" aria-hidden="true">
            {recap.weekendSeconds > 0 && <div className="bg-brand" style={{ flex: recap.weekendSeconds }}></div>}
            {recap.weekdaySeconds > 0 && <div className="bg-info" style={{ flex: recap.weekdaySeconds }}></div>}
          </div>
          <div className="pdx-num flex flex-wrap justify-between gap-3 text-[13px]">
            <span>
              <b className="font-semibold">{roundPercent(weekendPercent)}%</b>{" "}
              <span className="text-muted">weekend · {formatHours(recap.weekendSeconds, 1)}</span>
            </span>
            <span>
              <b className="font-semibold">{roundPercent(weekdayPercent)}%</b>{" "}
              <span className="text-muted">weekday · {formatHours(recap.weekdaySeconds, 1)}</span>
            </span>
          </div>
        </div>
      </div>
    </section>
  );
}

function ConsistencyCard({ recap }: { recap: RecapResponse }) {
  const totalDays = recap.daysPlayed + recap.daysNotPlayed;
  const playedPercent = totalDays > 0 ? (recap.daysPlayed / totalDays) * 100 : 0;
  const day = recap.mostActiveDay;
  const month = recap.mostActiveMonth;
  const tiles: Array<{ label: string; value: ReactNode }> = [
    { label: "Per day", value: <SessionsValue value={formatAverage(recap.averageSessionsPerDay)} /> },
    { label: "Per week", value: <SessionsValue value={formatAverage(recap.averageSessionsPerWeek)} /> },
    { label: "Per month", value: <SessionsValue value={formatAverage(recap.averageSessionsPerMonth)} /> },
    { label: "Most active day", value: day ? `${formatRecapDate(day.date)} · ${count(day.sessionCount)}` : "—" },
    {
      label: "Most active month",
      value: month ? `${formatRecapMonthLabel(month.month)} · ${month.sessionCount}` : "—",
    },
  ];

  return (
    <section className="pdx-card flex flex-col gap-6">
      <CardHeading
        kicker="Consistency"
        title="Days played"
        actions={
          <SideNote>
            {recap.daysPlayed} / {totalDays} days
          </SideNote>
        }
      />
      <div className="flex flex-wrap items-center gap-7">
        <div
          role="img"
          aria-label={`${roundPercent(playedPercent)}% of days played`}
          className="grid h-[140px] w-[140px] shrink-0 place-items-center rounded-full"
          style={{
            background: `conic-gradient(rgb(var(--bar)) 0 ${playedPercent}%, rgb(var(--track)) ${playedPercent}% 100%)`,
          }}
        >
          <div className="grid h-[108px] w-[108px] place-items-center rounded-full bg-surface text-center">
            <div>
              <p className="pdx-num text-[26px] font-semibold">{roundPercent(playedPercent)}%</p>
              <p className="text-[11px] uppercase tracking-[0.2em] text-muted">Played</p>
            </div>
          </div>
        </div>
        <dl className="flex flex-[1_1_160px] flex-col gap-2.5 text-sm">
          <KeyValue label="Days played" value={count(recap.daysPlayed)} />
          <KeyValue label="Days off" value={count(recap.daysNotPlayed)} />
          <KeyValue label="Longest streak" value={`${recap.longestStreakDays} days`} />
        </dl>
      </div>
      <div className="grid grid-cols-2 gap-3">
        {tiles.map((tile, index) => (
          <div
            key={tile.label}
            className={`min-w-0 rounded-[20px] border border-[color:var(--tile-border)] bg-tile px-[18px] py-4 ${
              index === tiles.length - 1 && tiles.length % 2 === 1 ? "col-span-2" : ""
            }`}
          >
            <p className="pdx-label">{tile.label}</p>
            <p className="pdx-num mt-1.5 truncate text-lg font-semibold">{tile.value}</p>
          </div>
        ))}
      </div>
    </section>
  );
}

function SessionsValue({ value }: { value: string }) {
  return (
    <>
      {value}
      <span className="font-medium text-muted"> sessions</span>
    </>
  );
}

function KeyValue({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="flex justify-between gap-4">
      <dt className="text-muted">{label}</dt>
      <dd className="pdx-num font-semibold">{value}</dd>
    </div>
  );
}

function SessionsCard({ recap }: { recap: RecapResponse }) {
  const longest = recap.gameWithLongestSession;
  const bestAverage = recap.topGameByAverageSession;
  const rows: LabeledValue[] = [
    { label: "Total sessions", value: count(recap.totalSessions) },
    { label: "Total time", value: formatHours(recap.totalSeconds, 1) },
    { label: "Median length", value: formatDuration(recap.medianSessionSeconds, "full") },
    {
      label: "Longest",
      value: `${formatDuration(recap.longestSessionSeconds, "full")}${longest ? ` · ${longest.title}` : ""}`,
    },
    { label: "Shortest", value: formatDuration(recap.shortestSessionSeconds, "full") },
    {
      label: "Best average",
      value: bestAverage ? `${bestAverage.title} · ${formatDuration(bestAverage.averageSessionSeconds, "full")}` : "—",
    },
  ];

  return (
    <section className="pdx-card flex flex-col gap-6">
      <CardHeading kicker="Sessions" title="Volume and length" />
      <div className="flex flex-wrap items-baseline gap-3">
        <span className="pdx-num text-[56px] font-semibold leading-none tracking-[-0.03em]">
          {formatDuration(recap.averageSessionSeconds, "full")}
        </span>
        <span className="text-sm text-muted">average session</span>
      </div>
      <dl className="flex flex-col">
        {rows.map((row) => (
          <div key={row.label} className="flex justify-between gap-4 border-t border-divider py-3 text-sm">
            <dt className="shrink-0 text-muted">{row.label}</dt>
            <dd className="pdx-num min-w-0 text-right font-semibold">{row.value}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}

function PlatformMixCard({ recap }: { recap: RecapResponse }) {
  const total = recap.platformTotals.reduce((sum, item) => sum + item.totalSeconds, 0);
  const platforms = [...recap.platformTotals]
    .sort((a, b) => b.totalSeconds - a.totalSeconds)
    .map((item) => ({ ...item, color: platformColor(item.platformKey, item.platformName) }));

  return (
    <section className="pdx-card flex flex-col gap-6">
      <CardHeading kicker="Platform mix" title="Share of playtime" actions={<SideNote>{rangeLabel(recap)}</SideNote>} />
      {platforms.length === 0 ? (
        <p className="pdx-dashed">No platform data available yet.</p>
      ) : (
        <>
          <StackedBar
            height={14}
            segments={platforms.map((item) => ({
              key: item.platformKey || item.platformName,
              label: item.platformName,
              value: item.totalSeconds,
              color: item.color,
            }))}
          />
          <div className="grid gap-3 [grid-template-columns:repeat(auto-fit,minmax(min(200px,100%),1fr))]">
            {platforms.map((item, index) => (
              <div
                key={`${item.platformKey}-${index}`}
                className="flex min-w-0 flex-col gap-1.5 rounded-[22px] border border-[color:var(--tile-border)] bg-tile px-[22px] py-5"
              >
                <div className="flex min-w-0 items-center gap-2 text-sm font-medium">
                  <Dot color={item.color} size={10} />
                  <span className="truncate">{item.platformName}</span>
                </div>
                <p className="pdx-num text-[32px] font-semibold tracking-[-0.02em]">
                  {roundPercent(total > 0 ? (item.totalSeconds / total) * 100 : 0)}%
                </p>
                <p className="pdx-num text-[13px] text-muted">
                  {formatHours(item.totalSeconds, 1)} · {count(item.sessionCount)} sessions
                </p>
              </div>
            ))}
          </div>
        </>
      )}
    </section>
  );
}

const LibraryColumns = "grid grid-cols-[26px_44px_minmax(0,2.4fr)_90px_100px_minmax(0,1.4fr)] items-center gap-4";

function LibraryCard({ recap, onOpenGame }: { recap: RecapResponse; onOpenGame: (gameId: string) => void }) {
  const binged = recap.mostBingedGame;
  const bySessions = recap.topGameBySessions;
  const chips: LabeledValue[] = [
    { label: "Unique games", value: count(recap.uniqueGames) },
    { label: "New games tried", value: count(recap.newGamesTried) },
    { label: "Abandoned", value: count(recap.abandonedGames) },
    { label: "Return rate games", value: count(recap.returnRateGames) },
    { label: "Top 3 share", value: `${roundPercent(recap.timeOnTop3Percent)}%` },
    { label: "Most binged", value: binged ? `${binged.title} · ${count(binged.sessionCount)}` : "—" },
    { label: "Top by sessions", value: bySessions ? `${bySessions.title} · ${count(bySessions.sessionCount)}` : "—" },
  ];
  const maxSeconds = Math.max(0, ...recap.gamesTable.map((game) => game.totalSeconds));

  return (
    <section className="pdx-card flex flex-col gap-6">
      <CardHeading kicker="Full library" title="Summed playtime by game" />
      <div className="flex flex-wrap gap-2.5">
        {chips.map((chip) => (
          <div
            key={chip.label}
            className="max-w-full truncate rounded-full border border-[color:var(--tile-border)] bg-tile px-4 py-2.5 text-[13px]"
          >
            <span className="text-muted">{chip.label}</span> <b className="pdx-num font-semibold">{chip.value}</b>
          </div>
        ))}
      </div>
      {recap.gamesTable.length === 0 ? (
        <p className="pdx-dashed">No games logged yet.</p>
      ) : (
        <div className="max-h-[640px] overflow-auto">
          <div role="table" aria-label="Playtime by game" className="flex min-w-[620px] flex-col">
            <div role="row" className={`${LibraryColumns} px-3.5 pb-2.5 text-[11px] font-medium tracking-[0.2em] text-muted`}>
              <span role="columnheader">#</span>
              <span role="columnheader" aria-label="Cover"></span>
              <span role="columnheader">GAME</span>
              <span role="columnheader" className="text-right">
                SESSIONS
              </span>
              <span role="columnheader" className="text-right">
                TIME
              </span>
              <span role="columnheader">SHARE</span>
            </div>
            {recap.gamesTable.map((game, index) => (
              <div key={index} role="row" className={`pdx-row pdx-num ${LibraryColumns} px-3.5 py-2`}>
                <span role="cell" className="text-[13px] text-muted">
                  {pad2(index + 1)}
                </span>
                <span role="cell">
                  <Cover title={game.title} src={game.capsuleUrl} className="h-11 w-11 rounded-xl" initialClassName="text-[15px]" />
                </span>
                <div role="cell" className="min-w-0">
                  <button
                    type="button"
                    onClick={() => onOpenGame(game.gameId)}
                    title={game.title}
                    className="block w-full truncate text-left text-[15px] font-medium transition hover:text-accent"
                  >
                    {game.title}
                  </button>
                  {game.isAbandoned && <p className="text-xs text-muted">abandoned</p>}
                </div>
                <span role="cell" className="text-right text-sm text-ink-2">
                  {count(game.sessionCount)}
                </span>
                <span role="cell" className="text-right text-sm font-semibold text-accent">
                  {formatHours(game.totalSeconds, 1)}
                </span>
                <span role="cell">
                  <ShareBar pct={maxSeconds > 0 ? (game.totalSeconds / maxSeconds) * 100 : 0} />
                </span>
              </div>
            ))}
          </div>
        </div>
      )}
    </section>
  );
}
