import { memo, useEffect, useRef, useState } from "react";
import { art, gameById, SOURCE_LABEL, weekByDay, weekdayInitial, type Game } from "../data/games";
import { focusedGame, useStore } from "../nav/store";

/* ---------- Controller glyphs ---------- */

type GlyphName = "A" | "B" | "X" | "Y" | "LB" | "RB" | "VIEW" | "MENU";

export function Glyph({ name }: { name: GlyphName }) {
  if (name === "LB" || name === "RB") return <span className="glyph glyph-bumper" aria-hidden>{name}</span>;
  if (name === "VIEW")
    return (
      <span className="glyph" aria-hidden>
        <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.8">
          <rect x="2" y="2" width="8" height="8" rx="1.5" />
          <rect x="6" y="6" width="8" height="8" rx="1.5" />
        </svg>
      </span>
    );
  if (name === "MENU")
    return (
      <span className="glyph" aria-hidden>
        <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round">
          <path d="M3 4.5h10M3 8h10M3 11.5h10" />
        </svg>
      </span>
    );
  return (
    <span className={`glyph${name === "A" ? " glyph-a" : ""}`} aria-hidden>
      {name}
    </span>
  );
}

export function Hint({ glyph, children }: { glyph: GlyphName; children: React.ReactNode }) {
  return (
    <span className="hint">
      <Glyph name={glyph} />
      {children}
    </span>
  );
}

/* ---------- Cover art with fallback ---------- */

type CoverKind = "capsule" | "portrait" | "hero";

export const Cover = memo(function Cover({
  game,
  kind,
  className = "",
  eager = false,
  compact = false,
}: {
  game: Game;
  kind: CoverKind;
  className?: string;
  eager?: boolean;
  /** Small thumbnails fall back to an initial instead of the full title. */
  compact?: boolean;
}) {
  const src = art[kind](game);
  const [failed, setFailed] = useState(false);
  return (
    <div className={`cover ${className}`}>
      {src && !failed ? (
        <img
          src={src}
          alt=""
          loading={eager ? "eager" : "lazy"}
          decoding="async"
          onLoad={(e) => e.currentTarget.setAttribute("data-loaded", "")}
          onError={() => setFailed(true)}
        />
      ) : compact ? (
        <div className="cover-initial" aria-hidden>
          {game.title.replace(/^The /, "").charAt(0)}
        </div>
      ) : (
        <div className="cover-fallback">
          <small>{SOURCE_LABEL[game.source]}</small>
          {game.title}
        </div>
      )}
    </div>
  );
});

/** Game logo image, or the title set in type when no logo exists. */
export function Logo({ game, className, titleClass }: { game: Game; className: string; titleClass: string }) {
  const src = art.logo(game);
  const [failed, setFailed] = useState(false);
  if (!src || failed) return <h1 className={titleClass}>{game.title}</h1>;
  return (
    <>
      <h1 className="sr-only">{game.title}</h1>
      <img className={className} src={src} alt="" decoding="async" onError={() => setFailed(true)} />
    </>
  );
}

/* ---------- Backdrop: hero art that follows focus with a two-layer crossfade ---------- */

export function Backdrop({ gameId, className = "" }: { gameId?: string; className?: string }) {
  const focused = useStore(focusedGame);
  const id = gameId ?? focused;
  const [layers, setLayers] = useState<{ a?: string; b?: string; on: "a" | "b" }>({ on: "a" });
  const pending = useRef(0);

  useEffect(() => {
    if (!id) return;
    const url = art.hero(gameById(id));
    const token = ++pending.current;
    // Short settle delay so fast scrolling does not decode every hero it passes.
    const t = window.setTimeout(() => {
      if (!url) {
        setLayers((l) => ({ ...l, [l.on]: undefined }));
        return;
      }
      const img = new Image();
      img.src = url;
      img
        .decode()
        .catch(() => undefined)
        .then(() => {
          if (token !== pending.current) return;
          setLayers((l) => {
            if (l[l.on] === url) return l;
            const next = l.on === "a" ? "b" : "a";
            return { ...l, [next]: url, on: next };
          });
        });
    }, 90);
    return () => window.clearTimeout(t);
  }, [id]);

  return (
    <div className={`backdrop ${className}`} aria-hidden>
      {layers.a && <img src={layers.a} alt="" data-on={layers.on === "a" ? "" : undefined} />}
      {layers.b && <img src={layers.b} alt="" data-on={layers.on === "b" ? "" : undefined} />}
    </div>
  );
}

/* ---------- This-week bars ---------- */

export function WeekBars({ maxHeight }: { maxHeight?: string }) {
  const max = Math.max(...weekByDay, 1);
  return (
    <div className="week" style={maxHeight ? ({ "--h-max": maxHeight } as React.CSSProperties) : undefined}>
      {weekByDay.map((m, i) => (
        <div key={i} className="week-col" data-today={i === 6 ? "" : undefined}>
          <div className="week-bar" title={`${m} min`}>
            <div className="week-fill" style={{ height: `${Math.max(m ? 6 : 0, (m / max) * 100)}%` }} />
          </div>
          <span className="week-day">{weekdayInitial(6 - i)}</span>
        </div>
      ))}
    </div>
  );
}

/* ---------- Live clock ---------- */

const clockFmt = new Intl.DateTimeFormat("en-GB", { hour: "2-digit", minute: "2-digit" });

export function Clock() {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const t = window.setInterval(() => setNow(new Date()), 15_000);
    return () => window.clearInterval(t);
  }, []);
  return <span className="num">{clockFmt.format(now)}</span>;
}
