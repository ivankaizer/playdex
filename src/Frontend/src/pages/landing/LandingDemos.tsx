import { useEffect, useState } from "react";
import { Cover, Dot } from "@/components/ui";
import { cx } from "@/lib/cx";
import { platformColor } from "@/lib/platforms";

/*
 * Small looping demos of the app for the landing. Everything here is sample data drawn to look like the
 * app's own rows; nothing is fetched. With reduced motion each demo holds its finished state instead.
 */

/** Steam library art for the sample games (portrait, 600px wide). */
export const steamCover = (appId: number) => `https://cdn.cloudflare.steamstatic.com/steam/apps/${appId}/library_600x900_2x.jpg`;

function prefersReducedMotion(): boolean {
  return typeof window !== "undefined" && !!window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;
}

/**
 * A step that counts 0..length-1 every `intervalMs` and wraps; held at `still` under reduced motion.
 * The step comes from the time elapsed, so a throttled timer skips ahead instead of slowing the demo.
 */
function useLoopTick(length: number, intervalMs: number, still = length - 1): number {
  const [reduced] = useState(prefersReducedMotion);
  const [tick, setTick] = useState(reduced ? still : 0);
  useEffect(() => {
    if (reduced) return;
    const start = Date.now();
    const id = window.setInterval(() => setTick(Math.floor((Date.now() - start) / intervalMs) % length), intervalMs);
    return () => window.clearInterval(id);
  }, [reduced, length, intervalMs]);
  return tick;
}

const syncFeed: ReadonlyArray<{ title: string; platform: string; platformKey: string; duration: string; cover: string }> = [
  { title: "Hades II", platform: "Steam", platformKey: "steam", duration: "1h 12m", cover: steamCover(1145350) },
  { title: "Ghost of Tsushima", platform: "PlayStation", platformKey: "psn", duration: "48m", cover: steamCover(2215430) },
  { title: "Forza Horizon 5", platform: "Xbox", platformKey: "xbox", duration: "2h 05m", cover: steamCover(1551360) },
  { title: "Helldivers 2", platform: "Steam", platformKey: "steam", duration: "1h 30m", cover: steamCover(553850) },
  { title: "Stellar Blade", platform: "PlayStation", platformKey: "psn", duration: "35m", cover: steamCover(3489700) },
];

/** Sessions from connected accounts arriving on their own, newest on top. */
export function SyncDemo() {
  const tick = useLoopTick(syncFeed.length, 2200);
  const visible = [0, 1, 2].map((offset) => syncFeed[(tick - offset + syncFeed.length) % syncFeed.length]);

  return (
    <div aria-hidden="true" className="flex flex-col gap-2.5">
      <div className="flex items-center justify-between px-1">
        <span className="pdx-label">Today</span>
        <span className="flex items-center gap-2 text-xs font-medium text-muted">
          <span className="pdx-live-dot"></span>
          Syncing
        </span>
      </div>
      {visible.map((session, index) => (
        <div
          key={`${session.title}-${tick}-${index}`}
          className={cx(
            "flex items-center gap-3 rounded-2xl bg-surface/80 px-3 py-2.5",
            index === 0 && "pdx-rise",
            index === 2 && "opacity-60",
          )}
        >
          <Cover title={session.title} src={session.cover} className="h-10 w-[30px] rounded-lg" initialClassName="text-xs" />
          <div className="flex min-w-0 flex-1 flex-col">
            <span className="truncate text-sm font-medium">{session.title}</span>
            <span className="flex items-center gap-1.5 text-xs text-muted">
              <Dot color={platformColor(session.platformKey, session.platform)} size={6} />
              {session.platform}
              {index === 0 && <span className="text-accent">· just now</span>}
            </span>
          </div>
          <span className="pdx-num text-sm font-semibold">{session.duration}</span>
        </div>
      ))}
    </div>
  );
}

const pasteText = "hades 2 for like 2h on saturday, then bg3 most of sunday";
const pasteDrafts: ReadonlyArray<{ title: string; meta: string; duration: string; cover: string }> = [
  { title: "Hades II", meta: "Steam · Sat", duration: "2h", cover: steamCover(1145350) },
  { title: "Baldur's Gate 3", meta: "Steam · Sun", duration: "5h 30m", cover: steamCover(1086940) },
];

/* Ticks of 50ms: type the note, think, draft two sessions, accept them, hold. */
const typedAt = pasteText.length;
const draftAt = [typedAt + 14, typedAt + 22];
const acceptedAt = typedAt + 40;
const pasteLength = typedAt + 90;

/** A note about the weekend turning into draft sessions that get accepted. */
export function PasteDemo() {
  const tick = useLoopTick(pasteLength, 50, acceptedAt);
  const typed = pasteText.slice(0, Math.min(tick, typedAt));
  const thinking = tick >= typedAt && tick < draftAt[0];
  const accepted = tick >= acceptedAt;

  return (
    <div aria-hidden="true" className="flex flex-col gap-2.5">
      <div className="min-h-[64px] rounded-2xl bg-surface/80 px-3.5 py-3 text-sm leading-snug text-ink-2">
        {typed}
        {tick < typedAt && <span className="ml-px inline-block h-4 w-px translate-y-0.5 animate-pulse bg-ink-2"></span>}
      </div>
      <div className="flex h-5 items-center gap-2 px-1 text-xs font-medium text-muted">
        {thinking && (
          <>
            <span className="pdx-live-dot"></span>Drafting sessions…
          </>
        )}
        {tick >= draftAt[0] && (accepted ? <span className="text-accent">✓ Added to your history</span> : "Review drafts")}
      </div>
      {pasteDrafts.map((draft, index) =>
        tick >= draftAt[index] ? (
          <div key={draft.title} className="pdx-rise flex items-center gap-3 rounded-2xl bg-surface/80 px-3 py-2.5">
            <Cover title={draft.title} src={draft.cover} className="h-10 w-[30px] rounded-lg" initialClassName="text-xs" />
            <div className="flex min-w-0 flex-1 flex-col">
              <span className="truncate text-sm font-medium">{draft.title}</span>
              <span className="text-xs text-muted">{draft.meta}</span>
            </div>
            <span className="pdx-num text-sm font-semibold">{draft.duration}</span>
            <span
              className={cx(
                "grid h-6 w-6 place-items-center rounded-full text-xs font-bold transition-colors duration-300",
                accepted ? "bg-brand text-white" : "bg-track text-faint",
              )}
            >
              ✓
            </span>
          </div>
        ) : (
          <div key={draft.title} className="h-[60px] rounded-2xl border border-dashed border-[color:var(--card-border)]"></div>
        ),
      )}
    </div>
  );
}

const pad = (value: number) => String(value).padStart(2, "0");
const clock = (seconds: number) => `${pad(Math.floor(seconds / 3600))}:${pad(Math.floor((seconds % 3600) / 60))}:${pad(seconds % 60)}`;

/** A live timer for a platform that doesn't sync. Stop saves it; Start runs a new one. */
export function TimerDemo() {
  const [running, setRunning] = useState(true);
  const [seconds, setSeconds] = useState(47 * 60 + 12);

  useEffect(() => {
    if (!running) return;
    const id = window.setInterval(() => setSeconds((value) => value + 1), 1000);
    return () => window.clearInterval(id);
  }, [running]);

  const toggle = () => {
    if (!running) setSeconds(0);
    setRunning(!running);
  };

  return (
    <div className="flex flex-col gap-2.5">
      <div className="flex items-center gap-3 rounded-2xl bg-surface/80 px-3 py-2.5">
        <Cover
          title="Super Mario Bros. Wonder"
          className="h-10 w-[30px] rounded-lg"
          initialClassName="text-xs"
          style={{ background: "linear-gradient(160deg,#f5a3b5,#e0455a)" }}
        />
        <div className="flex min-w-0 flex-1 flex-col">
          <span className="truncate text-sm font-medium">Super Mario Bros. Wonder</span>
          <span className="flex items-center gap-1.5 text-xs text-muted">
            <Dot color={platformColor("switch", "Switch")} size={6} />
            Switch
          </span>
        </div>
      </div>
      <div className="flex items-center justify-between gap-3 rounded-2xl bg-surface/80 px-4 py-3">
        <div className="flex flex-col">
          <span className="flex items-center gap-2 text-xs font-medium text-muted">
            {running ? (
              <>
                <span className="pdx-live-dot"></span>Playing
              </>
            ) : (
              <span className="text-accent">✓ Saved to history</span>
            )}
          </span>
          <span className="pdx-num text-[28px] font-semibold leading-tight tracking-[-0.02em]" aria-hidden="true">
            {clock(seconds)}
          </span>
        </div>
        <button
          type="button"
          onClick={toggle}
          className={cx(running ? "pdx-btn-outline" : "pdx-btn-primary", "!h-[38px] !px-5 !text-[13px]")}
        >
          {running ? "Stop" : "Start"}
        </button>
      </div>
    </div>
  );
}
