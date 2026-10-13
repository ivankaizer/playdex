import { Info, Play } from "lucide-react";
import { launch, openGame } from "../app-state";
import { Backdrop, Cover, Logo, WeekBars } from "../components/shared";
import {
  fmtHours,
  fmtMinutes,
  gameById,
  lastPlayedDay,
  lastPlayedText,
  recentGames,
  relativeDay,
  SOURCE_LABEL,
  statsFor,
  weekMinutes,
} from "../data/games";
import { focusedGame, useStore } from "../nav/store";
import "./dossier.css";

/** Home B: the website's two-card dashboard at TV scale. The Now card becomes whatever the list focuses. */
export function DossierHome() {
  return (
    <main className="dossier">
      <NowCard />
      <section className="card recent" aria-labelledby="recent-h">
        <header className="recent-head">
          <h2 id="recent-h" className="section-h">
            Recent
          </h2>
          <span className="label">{recentGames.length} games</span>
        </header>
        <ol className="recent-list" data-scroll="y" data-nav-group="recent">
          {recentGames.map((r, i) => (
            <li key={r.game.id}>
              <button
                className="rrow"
                data-nav
                data-nav-default={i === 0 ? "" : undefined}
                data-game={r.game.id}
                data-kind="game"
                onClick={() => launch(r.game.id)}
              >
                <Cover game={r.game} kind="portrait" className="rrow-thumb" compact />
                <span className="rrow-text">
                  <span className="rrow-title">{r.game.title}</span>
                  <span className="rrow-sub">
                    {lastPlayedText(r)} · {SOURCE_LABEL[r.game.source]}
                  </span>
                </span>
                <span className="rrow-val num">{r.weekMinutes ? fmtMinutes(r.weekMinutes) : "—"}</span>
              </button>
            </li>
          ))}
        </ol>
        <footer className="recent-week">
          <div className="recent-week-head">
            <span className="label">Last 7 days</span>
            <span className="rrow-val num">{fmtMinutes(weekMinutes)}</span>
          </div>
          <WeekBars maxHeight="2.6rem" />
        </footer>
      </section>
    </main>
  );
}

function NowCard() {
  const focused = useStore(focusedGame);
  const id = focused ?? recentGames[0].game.id;
  const game = gameById(id);
  const st = statsFor(id)!;
  const isLatest = id === recentGames[0].game.id;
  const lastDay = lastPlayedDay(st);

  return (
    <section className="card now" aria-live="polite" aria-label="Selected game">
      <div className="now-art">
        <Backdrop gameId={id} />
        <div className="now-scrim" />
      </div>
      <div className="now-body" key={id}>
        <span className="kicker">{isLatest && st.lastSession ? "Continue playing" : lastDay === undefined ? "Ready to play" : `Last played ${relativeDay(lastDay).toLowerCase()}`}</span>
        <Logo game={game} className="now-logo" titleClass="now-title" />
        <div className="now-stats">
          <div className="tile now-stat">
            <span className="label">Last 7 days</span>
            <b className="num">{st.weekMinutes ? fmtMinutes(st.weekMinutes) : "—"}</b>
          </div>
          <div className="tile now-stat">
            <span className="label">Total</span>
            <b className="num">{fmtHours(game.totalMinutes)}</b>
          </div>
          <div className="tile now-stat">
            <span className="label">Last session</span>
            <b className="num">{st.lastSession ? fmtMinutes(st.lastSession.minutes) : "—"}</b>
          </div>
          <div className="tile now-stat">
            <span className="label">Store</span>
            <b>{SOURCE_LABEL[game.source]}</b>
          </div>
        </div>
        <div className="now-actions" data-nav-group="now-actions">
          <button className="btn btn-primary btn-lg" data-nav data-kind="continue" data-game={id} onClick={() => launch(id)}>
            <Play fill="currentColor" /> {isLatest && st.lastSession ? "Continue" : "Play"}
          </button>
          <button className="btn btn-outline btn-lg" data-nav data-kind="details" data-game={id} onClick={() => openGame(id)}>
            <Info /> Details
          </button>
        </div>
      </div>
    </section>
  );
}
