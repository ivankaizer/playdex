import { Flame } from "lucide-react";
import { Cover, Logo, WeekBars } from "../components/shared";
import { fmtMinutes, lastPlayedDay, lastPlayedText, SOURCE_LABEL, useRecentGames, type GameStats } from "../data/games";
import { playdexConnected, playdexSummary } from "../data/playdex";
import { useStore } from "../nav/store";
import { PlayButton } from "./parts";
import { goTo, openGamePage, totalMinutes } from "./state";

const minutes = (seconds: number) => Math.round(seconds / 60);

/** Shelf columns: the last played games by day. */
const SHELF = [
  { label: "Today", test: (d: number) => d === 0 },
  { label: "Yesterday", test: (d: number) => d === 1 },
  { label: "This week", test: (d: number) => d >= 2 && d <= 6 },
  { label: "Earlier", test: (d: number) => d > 6 },
];
const SHELF_MAX = 10;

/**
 * Home in desktop mode: the last played game large with the next two beside it, the week from Playdex, and a shelf of
 * what was played recently, by day.
 */
export function Home() {
  const recent = useRecentGames();
  if (recent.length === 0) return <EmptyHome />;
  const [lead, ...rest] = recent;
  const shelf = recent.filter((r) => lastPlayedDay(r) !== undefined).slice(0, SHELF_MAX);

  return (
    <div className="dk-page dk-home">
      <section className="dk-jump" aria-labelledby="dk-jump">
        <h2 id="dk-jump" className="dk-h2">
          Jump back in
        </h2>
        <div className="dk-jump-grid" data-nav-group="home-jump">
          <LeadCard stats={lead} />
          {rest.length > 0 && (
            <div className="dk-jump-side">
              {rest.slice(0, 2).map((r) => (
                <SideCard key={r.game.id} stats={r} />
              ))}
            </div>
          )}
        </div>
      </section>

      <Week />

      {shelf.length > 0 && (
        <section aria-labelledby="dk-recent">
          <div className="dk-section-head">
            <h2 id="dk-recent" className="dk-h2">
              Recently played
            </h2>
            <button className="dk-link" onClick={() => goTo("Library")}>
              All games
            </button>
          </div>
          <div className="dk-shelf" data-scroll="x" data-nav-group="home-shelf">
            {SHELF.map((col) => {
              const list = shelf.filter((r) => col.test(lastPlayedDay(r)!));
              if (!list.length) return null;
              return (
                <div key={col.label} className="dk-shelf-col">
                  <span className="dk-label">{col.label}</span>
                  <div className="dk-shelf-row">
                    {list.map((r) => (
                      <button
                        key={r.game.id}
                        className="dk-shelf-item"
                        data-nav
                        data-kind="cover"
                        data-game={r.game.id}
                        onClick={() => openGamePage(r.game.id)}
                      >
                        <Cover game={r.game} kind="portrait" className="dk-shelf-art" />
                        <span>{r.game.title}</span>
                      </button>
                    ))}
                  </div>
                </div>
              );
            })}
          </div>
        </section>
      )}
    </div>
  );
}

function LeadCard({ stats }: { stats: GameStats }) {
  const { game } = stats;
  const total = totalMinutes(useStore(playdexSummary), game);
  return (
    <article className="dk-lead">
      <Cover game={game} kind="hero" className="dk-card-art" eager untitled />
      <div className="dk-lead-scrim" />
      <button
        className="dk-stretch"
        data-nav
        data-kind="details"
        data-game={game.id}
        aria-label={`Open ${game.title}`}
        onClick={() => openGamePage(game.id)}
      />
      <div className="dk-lead-body">
        <span className="dk-kicker">Last played · {lastPlayedText(stats)}</span>
        <Logo game={game} className="dk-lead-logo" titleClass="dk-lead-title" />
        <span className="dk-lead-meta">
          {total ? (
            <>
              <b className="num">{fmtMinutes(total)}</b> played ·{" "}
            </>
          ) : null}
          {SOURCE_LABEL[game.source]}
        </span>
        <div className="dk-lead-actions">
          <PlayButton game={game} label="Continue" />
        </div>
      </div>
    </article>
  );
}

function SideCard({ stats }: { stats: GameStats }) {
  const { game } = stats;
  const total = totalMinutes(useStore(playdexSummary), game);
  return (
    <article className="dk-side-card">
      <Cover game={game} kind="hero" className="dk-card-art" />
      <div className="dk-side-card-scrim" />
      <button
        className="dk-stretch"
        data-nav
        data-kind="details"
        data-game={game.id}
        aria-label={`Open ${game.title}`}
        onClick={() => openGamePage(game.id)}
      />
      <div className="dk-side-card-body">
        <div className="dk-side-card-text">
          <h3>{game.title}</h3>
          <span>
            {lastPlayedText(stats)}
            {total ? ` · ${fmtMinutes(total)}` : ""} · {SOURCE_LABEL[game.source]}
          </span>
        </div>
        <PlayButton game={game} round />
      </div>
    </article>
  );
}

/** The last 7 days from Playdex: total, by day, sessions, streak and the most played game. */
function Week() {
  const summary = useStore(playdexSummary);
  if (!summary) {
    if (playdexConnected) return null;
    return (
      <section className="dk-card dk-week dk-week-off" aria-label="This week">
        <p>Connect Playdex to see your week, your streak and every session in one place.</p>
        <button className="dk-btn dk-btn-outline" data-nav data-kind="account" onClick={() => goTo("Settings")}>
          Add your API key
        </button>
      </section>
    );
  }
  const top = summary.topGames[0];
  return (
    <section className="dk-card dk-week" aria-label="This week">
      <div className="dk-stat">
        <span className="dk-label">Last 7 days</span>
        <span className="dk-week-total num">{summary.week.totalSeconds ? fmtMinutes(minutes(summary.week.totalSeconds)) : "—"}</span>
      </div>
      <div className="dk-week-bars">
        <WeekBars maxHeight="2.6rem" minutes={summary.days.map((d) => minutes(d.totalSeconds))} />
      </div>
      <div className="dk-stat">
        <span className="dk-label">Sessions</span>
        <span className="dk-stat-value num">{summary.week.sessionCount}</span>
      </div>
      <div className="dk-stat">
        <span className="dk-label">Streak</span>
        <span className="dk-stat-value num dk-streak">
          <Flame aria-hidden />
          {summary.currentStreakDays === 1 ? "1 day" : `${summary.currentStreakDays} days`}
        </span>
      </div>
      {top && (
        <div className="dk-stat dk-week-top">
          <span className="dk-label">Most played</span>
          <span className="dk-stat-value">
            {top.title} · <span className="num">{fmtMinutes(minutes(top.totalSeconds))}</span>
          </span>
        </div>
      )}
      <button className="dk-link dk-week-link" onClick={() => goTo("Playdex")}>
        Open Playdex
      </button>
    </section>
  );
}

function EmptyHome() {
  return (
    <div className="dk-page dk-empty">
      <div className="dk-card">
        <span className="dk-kicker">No games yet</span>
        <h2>Install a game on Steam or add one in Manage</h2>
        <p className="dk-muted">Installed Steam games and the games you add to Playdex show up here.</p>
        <button className="dk-btn dk-btn-primary" data-nav data-nav-default data-kind="desk" onClick={() => goTo("Manage")}>
          Open Manage
        </button>
      </div>
    </div>
  );
}
