import { Flame } from "lucide-react";
import { useEffect, useState, type ReactNode } from "react";
import { openGame, showToast } from "../app-state";
import { WeekBars } from "../components/shared";
import { fmtMinutes } from "../data/games";
import {
  daysAgo,
  libraryGame,
  playdexConnected,
  playdexSummary,
  type PlaydexPeriodDetail,
  type PlaydexSession,
  type PlaydexSummary,
} from "../data/playdex";
import { loadRecap, recaps, type Recap } from "../data/recap";
import { useStore } from "../nav/store";
import "./playdex.css";

const minutes = (seconds: number) => Math.round(seconds / 60);
const clock = new Intl.DateTimeFormat("en-GB", { hour: "2-digit", minute: "2-digit" });
const monthName = new Intl.DateTimeFormat("en", { month: "long" });
const WEEKDAYS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

/* As on the web: an overview of now, and the yearly recap. */
const VIEWS = [
  { id: "overview", label: "Overview" },
  { id: "recap", label: "Recap" },
] as const;
type View = (typeof VIEWS)[number]["id"];

function remembered<T extends string>(key: string, allowed: readonly T[], fallback: T): T {
  try {
    const v = sessionStorage.getItem(key);
    return allowed.includes(v as T) ? (v as T) : fallback;
  } catch {
    return fallback;
  }
}

function remember(key: string, value: string) {
  try {
    sessionStorage.setItem(key, value);
  } catch {
    /* storage unavailable */
  }
}

/**
 * The Playdex tab: the user's Playdex history, every platform Playdex records, not only this PC. Overview is today,
 * the last 7 days and this month (from the launcher summary); Recap is this year or last year as the web's Recap
 * page computes it. A game on this PC opens its game page.
 */
export function PlaydexScreen() {
  const summary = useStore(playdexSummary);
  const [view, setView] = useState<View>(() => remembered("pdx.view", ["overview", "recap"], "overview"));
  const thisYear = new Date().getFullYear();
  const years = [thisYear, thisYear - 1];
  const [year, setYear] = useState(() => Number(remembered("pdx.year", years.map(String), String(thisYear))));

  return (
    <main className="pdx">
      <header className="pdx-bar">
        <div className="seg" role="tablist" aria-label="Playdex" data-nav-group="pdx-view">
          {VIEWS.map((v) => (
            <button
              key={v.id}
              role="tab"
              className="seg-btn"
              aria-selected={view === v.id}
              data-nav
              data-kind="filter"
              onClick={() => {
                setView(v.id);
                remember("pdx.view", v.id);
              }}
            >
              {v.label}
            </button>
          ))}
        </div>
        {view === "recap" && (
          <div className="seg" role="group" aria-label="Year" data-nav-group="pdx-year">
            {years.map((y, i) => (
              <button
                key={y}
                className="seg-btn"
                aria-pressed={year === y}
                data-nav
                data-kind="filter"
                onClick={() => {
                  setYear(y);
                  remember("pdx.year", String(y));
                }}
              >
                {i === 0 ? "This year" : "Last year"} <span className="lib-count num">{y}</span>
              </button>
            ))}
          </div>
        )}
      </header>
      {!playdexConnected ? (
        <Message
          title="Connect Playdex to see your stats"
          text="Add your Playdex API key in Settings → Your Playdex account. The key and the server address are on the Playdex website under Settings → Integrations."
        />
      ) : view === "overview" ? (
        summary ? (
          <Overview summary={summary} />
        ) : (
          <Message title="Your Playdex stats are on their way" text="They show up here once this PC reaches Playdex." />
        )
      ) : (
        <RecapView key={year} year={year} summary={summary} />
      )}
    </main>
  );
}

function Message({ title, text }: { title: string; text: string }) {
  return (
    <div className="pdx-message">
      <div className="card">
        <span className="kicker">Playdex</span>
        <h2>{title}</h2>
        <p>{text}</p>
      </div>
    </div>
  );
}

function open(summary: PlaydexSummary | null, playdexGameId: string) {
  const local = libraryGame(summary, playdexGameId);
  if (local) openGame(local.id);
  else showToast("This game isn't on this PC.");
}

/* ---------- Overview: today, the last 7 days, this month ---------- */

function Overview({ summary }: { summary: PlaydexSummary }) {
  const periods = summary.periods;
  // Servers older than the period details send only today's and the week's totals.
  const today: PlaydexPeriodDetail = periods?.today ?? { ...summary.today, topGames: [], platforms: [] };
  const week: PlaydexPeriodDetail = periods?.week ?? { ...summary.week, topGames: summary.topGames, platforms: [] };
  const todaySessions = summary.recentSessions.filter((s) => daysAgo(s.startedAt) === 0);
  const streak = summary.currentStreakDays;

  return (
    <div className="pdx-periods">
      <Period
        title="Today"
        detail={today}
        summary={summary}
        badge={
          streak > 1 && (
            <span className="rail-streak num">
              <Flame aria-hidden /> {streak}-day streak
            </span>
          )
        }
      >
        {todaySessions.length > 0 && <Sessions sessions={todaySessions} summary={summary} />}
      </Period>
      <Period title="Last 7 days" detail={week} summary={summary}>
        <WeekBars maxHeight="4.8rem" minutes={summary.days.map((d) => minutes(d.totalSeconds))} />
      </Period>
      {periods?.month && summary.monthDays ? (
        <Period title="This month" note={monthName.format(new Date())} detail={periods.month} summary={summary}>
          <Bars
            height="4.8rem"
            values={summary.monthDays.map((d) => d.totalSeconds)}
            labels={summary.monthDays.map((_, i) => (i === 0 || (i + 1) % 7 === 0 ? String(i + 1) : ""))}
            highlight={summary.monthDays.length - 1}
          />
        </Period>
      ) : (
        <section className="card pdx-card">
          <span className="kicker">This month</span>
          <p className="pdx-sub">This month shows up once the Playdex server is updated.</p>
        </section>
      )}
    </div>
  );
}

function Period({
  title,
  note,
  badge,
  detail,
  summary,
  children,
}: {
  title: string;
  note?: string;
  badge?: ReactNode;
  detail: PlaydexPeriodDetail;
  summary: PlaydexSummary;
  children?: ReactNode;
}) {
  const capsule = new Map(summary.games.map((g) => [g.gameId, g.capsuleUrl]));
  const most = Math.max(detail.topGames[0]?.totalSeconds ?? 0, 1);
  return (
    <section className="card pdx-card pdx-period" aria-label={title} data-scroll="y">
      <div className="pdx-head">
        <span className="kicker">{title}</span>
        {badge || (note && <span className="pdx-sub">{note}</span>)}
      </div>
      <p className="pdx-big num">{fmtMinutes(minutes(detail.totalSeconds))}</p>
      <p className="pdx-sub num">
        {detail.sessionCount === 0
          ? "Nothing played"
          : `${detail.sessionCount} ${detail.sessionCount === 1 ? "session" : "sessions"}${detail.topGames.length ? ` · ${detail.topGames.length} ${detail.topGames.length === 1 ? "game" : "games"}` : ""}`}
      </p>
      {children}
      {detail.platforms.length > 0 && <Platforms platforms={detail.platforms} />}
      {detail.topGames.length > 0 && (
        <div className="pdx-block">
          <span className="label">Most played</span>
          <div className="pdx-rows" data-nav-group={`pdx-top-${title}`}>
            {detail.topGames.map((g) => (
              <GameRow
                key={g.gameId}
                summary={summary}
                gameId={g.gameId}
                title={g.title}
                capsule={capsule.get(g.gameId) ?? null}
                share={g.totalSeconds / most}
                detail={`${g.sessionCount} ${g.sessionCount === 1 ? "session" : "sessions"}`}
                value={fmtMinutes(minutes(g.totalSeconds))}
              />
            ))}
          </div>
        </div>
      )}
    </section>
  );
}

function Sessions({ sessions, summary }: { sessions: PlaydexSession[]; summary: PlaydexSummary }) {
  const capsule = new Map(summary.games.map((g) => [g.gameId, g.capsuleUrl]));
  return (
    <div className="pdx-block">
      <span className="label">Sessions</span>
      <div className="pdx-rows" data-nav-group="pdx-today-sessions">
        {sessions.map((s) => (
          <GameRow
            key={s.sessionId}
            summary={summary}
            gameId={s.gameId}
            title={s.title}
            capsule={capsule.get(s.gameId) ?? null}
            detail={`${s.platformName ?? s.platformKey} · ${clock.format(new Date(s.startedAt))}`}
            value={s.endedAt ? fmtMinutes(minutes(s.durationSeconds)) : "Playing"}
          />
        ))}
      </div>
    </div>
  );
}

/** Time per platform as one bar, with a legend. */
function Platforms({ platforms }: { platforms: { platformKey: string; platformName: string; totalSeconds: number }[] }) {
  const total = Math.max(
    platforms.reduce((t, p) => t + p.totalSeconds, 0),
    1,
  );
  return (
    <div className="pdx-block">
      <span className="label">Platforms</span>
      <div className="pdx-share" aria-hidden>
        {platforms.map((p, i) => (
          <i key={p.platformKey} data-tone={i % 4} style={{ width: `${(p.totalSeconds / total) * 100}%` }} />
        ))}
      </div>
      <ul className="pdx-legend">
        {platforms.map((p, i) => (
          <li key={p.platformKey}>
            <span className="pdx-dot" data-tone={i % 4} aria-hidden />
            <span className="pdx-legend-name">{p.platformName}</span>
            <span className="pdx-sub num">
              {fmtMinutes(minutes(p.totalSeconds))} · {Math.round((p.totalSeconds / total) * 100)}%
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function GameRow({
  summary,
  gameId,
  title,
  capsule,
  share,
  detail,
  value,
}: {
  summary: PlaydexSummary | null;
  gameId: string;
  title: string;
  capsule: string | null;
  share?: number;
  detail: string;
  value: string;
}) {
  const local = libraryGame(summary, gameId);
  return (
    <button
      className="pdx-row"
      data-nav
      data-kind={local ? "details" : "pdx-history"}
      data-game={local?.id}
      onClick={() => open(summary, gameId)}
    >
      <span className="rail-thumb pdx-thumb" aria-hidden>
        {capsule ? <img src={capsule} alt="" loading="lazy" /> : title.charAt(0)}
      </span>
      <span className="pdx-main">
        <span className="pdx-name">{title}</span>
        {share !== undefined && (
          <span className="rail-meter">
            <i style={{ width: `${share * 100}%` }} />
          </span>
        )}
        <span className="pdx-sub num">{detail}</span>
      </span>
      <span className="pdx-val num">{value}</span>
    </button>
  );
}

/** Columns of values, the highlighted one in the accent colour; empty labels leave the axis quiet. */
function Bars({ values, labels, highlight, height }: { values: number[]; labels: string[]; highlight?: number; height: string }) {
  const max = Math.max(...values, 1);
  return (
    <div className="pdx-bars" style={{ "--n": values.length, "--h-max": height } as React.CSSProperties}>
      {values.map((v, i) => (
        <div key={i} className="week-col" data-today={i === highlight ? "" : undefined} style={{ "--i": i } as React.CSSProperties}>
          <div className="week-bar">
            <div className="week-fill" style={{ height: `${Math.max(v ? 6 : 0, (v / max) * 100)}%` }} />
          </div>
          <span className="week-day">{labels[i]}</span>
        </div>
      ))}
    </div>
  );
}

/* ---------- Recap: a year, as the web's Recap page ---------- */

function RecapView({ year, summary }: { year: number; summary: PlaydexSummary | null }) {
  const state = useStore(recaps)[year];
  useEffect(() => loadRecap(year), [year]);

  if (!state || state.status === "loading") return <div className="pdx-message pdx-loading">Loading your {year} recap…</div>;
  if (state.status === "unavailable") {
    return (
      <Message title={`Your ${year} recap couldn't be loaded`} text="Check that this PC can reach Playdex, then open the tab again." />
    );
  }
  const recap = state.recap;
  if (recap.totalSessions === 0) return <Message title={`Nothing played in ${year}`} text="Sessions recorded to Playdex show up here." />;
  return <RecapStats year={year} recap={recap} summary={summary} />;
}

function RecapStats({ year, recap, summary }: { year: number; recap: Recap; summary: PlaydexSummary | null }) {
  const most = Math.max(recap.topGamesByTime[0]?.totalSeconds ?? 0, 1);
  const weekdays = WEEKDAYS.map((_, i) => recap.sessionsByWeekday.find((d) => d.weekday === i)?.sessionCount ?? 0);
  const hours = Array.from({ length: 24 }, (_, h) => recap.sessionsByHour.find((d) => d.hour === h)?.sessionCount ?? 0);
  const weekend =
    recap.weekendSeconds + recap.weekdaySeconds > 0 ? recap.weekendSeconds / (recap.weekendSeconds + recap.weekdaySeconds) : 0;
  const busiestDay = weekdays.indexOf(Math.max(...weekdays));

  return (
    <div className="pdx-recap">
      <section className="card pdx-card" aria-label={`${year} in numbers`}>
        <span className="kicker">{year} recap</span>
        <p className="pdx-big num">{fmtMinutes(minutes(recap.totalSeconds))}</p>
        <p className="pdx-sub num">played across {recap.totalSessions} sessions</p>
        <dl className="pdx-facts num">
          <Fact label="Days played" value={`${recap.daysPlayed}`} />
          <Fact label="Longest streak" value={`${recap.longestStreakDays} ${recap.longestStreakDays === 1 ? "day" : "days"}`} />
          <Fact label="Average session" value={fmtMinutes(minutes(recap.averageSessionSeconds))} />
          <Fact label="Games" value={`${recap.uniqueGames}`} />
          <Fact label="New games" value={`${recap.newGamesTried}`} />
          <Fact label="Weekends" value={`${Math.round(weekend * 100)}%`} />
        </dl>
        <div className="pdx-block">
          <span className="label">Weekly rhythm</span>
          <Bars height="4.4rem" values={weekdays} labels={WEEKDAYS.map((d) => d.charAt(0))} highlight={busiestDay} />
          <p className="pdx-sub">Sessions by day{weekdays[busiestDay] > 0 ? ` · most on ${WEEKDAYS[busiestDay]}` : ""}</p>
        </div>
      </section>

      <section className="card pdx-card" aria-label="Most played" data-scroll="y">
        <span className="kicker">Most played</span>
        <div className="pdx-rows" data-nav-group="pdx-recap-games">
          {recap.topGamesByTime.map((g) => (
            <GameRow
              key={g.gameId}
              summary={summary}
              gameId={g.gameId}
              title={g.title}
              capsule={g.capsuleUrl}
              share={g.totalSeconds / most}
              detail={`${g.sessionCount} ${g.sessionCount === 1 ? "session" : "sessions"} · ${fmtMinutes(minutes(g.averageSessionSeconds))} each`}
              value={fmtMinutes(minutes(g.totalSeconds))}
            />
          ))}
        </div>
        <div className="pdx-block">
          <span className="label">Hourly activity</span>
          <Bars
            height="4.4rem"
            values={hours}
            labels={hours.map((_, h) => (h % 3 === 0 ? String(h).padStart(2, "0") : ""))}
            highlight={recap.peakHour?.hour}
          />
          <p className="pdx-sub">Sessions by starting hour</p>
        </div>
      </section>

      <section className="card pdx-card" aria-label="Highlights">
        <span className="kicker">Highlights</span>
        <dl className="pdx-highlights">
          {recap.mostActiveMonth && (
            <Fact
              label="Busiest month"
              value={monthName.format(new Date(`${recap.mostActiveMonth.month}-15T12:00:00`))}
              note={`${recap.mostActiveMonth.sessionCount} sessions`}
            />
          )}
          {recap.gameWithLongestSession && (
            <Fact
              label="Longest session"
              value={fmtMinutes(minutes(recap.gameWithLongestSession.sessionSeconds))}
              note={recap.gameWithLongestSession.title}
            />
          )}
          {recap.mostBingedGame && (
            <Fact label="Most returned to" value={recap.mostBingedGame.title} note={`${recap.mostBingedGame.sessionCount} sessions`} />
          )}
          {recap.peakHour && <Fact label="Favourite hour" value={`${String(recap.peakHour.hour).padStart(2, "0")}:00`} />}
        </dl>
        {recap.platformTotals.length > 0 && (
          <Platforms platforms={[...recap.platformTotals].sort((a, b) => b.totalSeconds - a.totalSeconds)} />
        )}
      </section>
    </div>
  );
}

function Fact({ label, value, note }: { label: string; value: string; note?: string }) {
  return (
    <div className="pdx-fact">
      <dt className="label">{label}</dt>
      <dd>
        <span className="pdx-fact-value">{value}</span>
        {note && <span className="pdx-sub">{note}</span>}
      </dd>
    </div>
  );
}
