import type { ReactNode } from "react";
import { Link } from "react-router";
import { AppFooter } from "@/components/AppFooter";
import { PlatformChips } from "@/components/PlatformChips";
import { BarChart, CardHeading, Cover, Donut, LegendRow, StatTile, type BarDatum, type DonutSegment } from "@/components/ui";
import { formatHoursShort } from "@/lib/format";
import { platformColor } from "@/lib/platforms";
import { dashUrl } from "./dashUrl";
import { LandingHeader } from "./LandingHeader";

export { dashUrl };

/*
 * Landing-page preview of the recap. These are the page's illustrative sample
 * figures (shown under a "Preview" kicker), not the visitor's data.
 */
const previewKpis: ReadonlyArray<{ label: string; value: string }> = [
  { label: "Hours played", value: "124.8h" },
  { label: "Sessions", value: "93" },
  { label: "Top title", value: "Hades II" },
];

const previewTotalHours = "124.8h";

const previewPlatforms: ReadonlyArray<{ key: string; label: string; value: number; hours: string }> = [
  { key: "steam", label: "Steam", value: 52, hours: "64.9h" },
  { key: "ps5", label: "PlayStation", value: 30, hours: "37.4h" },
  { key: "xbox-series-xs", label: "Xbox", value: 12, hours: "14.9h" },
  { key: "switch-1", label: "Switch", value: 6, hours: "7.6h" },
];

/** Steam library art for the sample games (portrait, 600px wide). */
const steamCover = (appId: number) => `https://cdn.cloudflare.steamstatic.com/steam/apps/${appId}/library_600x900_2x.jpg`;

const previewTopGames: ReadonlyArray<{ gameId: string; title: string; hours: number; cover: string }> = [
  { gameId: "hades-2", title: "Hades II", hours: 26.4, cover: steamCover(1145350) },
  { gameId: "bg3", title: "Baldur's Gate 3", hours: 18.2, cover: steamCover(1086940) },
  { gameId: "re4", title: "Resident Evil 4", hours: 14.7, cover: steamCover(2050650) },
  { gameId: "helldivers-2", title: "Helldivers 2", hours: 12.6, cover: steamCover(553850) },
  { gameId: "fh5", title: "Forza Horizon 5", hours: 10.8, cover: steamCover(1551360) },
];

const coverFallbacks: readonly string[] = [
  "linear-gradient(160deg,#f5a3b5,#e5a8f0 45%,#4a4f63)",
  "linear-gradient(160deg,#f5d98f,#f2bfa4 45%,#4a4f63)",
  "linear-gradient(160deg,#9fe8cf,#a6dcd8 45%,#4a4f63)",
  "linear-gradient(160deg,#a6dcf5,#a3d8ea 45%,#4a4f63)",
  "linear-gradient(160deg,#d3c8f7,#b5b8f2 45%,#4a4f63)",
];


const donutSegments: readonly DonutSegment[] = previewPlatforms.map((platform) => ({
  key: platform.key,
  label: platform.label,
  value: platform.value,
  color: platformColor(platform.key, platform.label),
}));

const peakGame = previewTopGames.reduce((best, game) => (game.hours > best.hours ? game : best), previewTopGames[0]);

const topGameBars: readonly BarDatum[] = previewTopGames.map((game) => ({
  key: game.gameId,
  value: game.hours,
  label: game.title,
  title: `${game.title} · ${game.hours}h`,
  highlight: game.gameId === peakGame.gameId,
}));

const iconProps = {
  "aria-hidden": true,
  viewBox: "0 0 24 24",
  className: "h-6 w-6",
  fill: "none",
  stroke: "currentColor",
  strokeWidth: 1.8,
  strokeLinecap: "round",
  strokeLinejoin: "round",
} as const;

const features: ReadonlyArray<{ title: string; body: string; icon: ReactNode }> = [
  {
    title: "Steam, PlayStation and Xbox, synced for you",
    body: "Connect once and every session shows up on its own. No timers to start, nothing to forget.",
    icon: (
      <svg {...iconProps}>
        <path d="M21 12a9 9 0 1 1-2.64-6.36"></path>
        <path d="M21 3v6h-6"></path>
      </svg>
    ),
  },
  {
    title: "Log a whole weekend in one paste",
    body: "Describe what you played or drop a screenshot. AI turns it into sessions you review and accept.",
    icon: (
      <svg {...iconProps}>
        <path d="M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9z"></path>
        <path d="M19 15l.8 2.2L22 18l-2.2.8L19 21l-.8-2.2L16 18l2.2-.8z"></path>
      </svg>
    ),
  },
  {
    title: "Your year in games, on demand",
    body: "Peak hours, longest streak, most-binged title and where the time really went, for any year or range.",
    icon: (
      <svg {...iconProps}>
        <path d="M4 20V10M10 20V4M16 20v-7M22 20H2"></path>
      </svg>
    ),
  },
  {
    title: "Switch, retro, anything else",
    body: "Start a live timer or log past sessions for platforms that don't sync, and they count like the rest.",
    icon: (
      <svg {...iconProps}>
        <circle cx="12" cy="13" r="8"></circle>
        <path d="M12 9v4l2.5 1.5M10 2h4"></path>
      </svg>
    ),
  },
];

/** playdex.dev: the public landing, a static page served from Cloudflare Pages. */
export function LandingPage() {
  return (
    <main className="pdx-page relative px-4 pb-10 pt-6 sm:px-8 sm:pt-8">
      <div className="relative mx-auto flex w-full max-w-[1180px] flex-col gap-7">
        <LandingHeader />

        <section className="flex flex-col gap-7 px-2 pb-6 pt-6 sm:pt-10">
          <h1 className="m-0 max-w-[16em] text-[clamp(40px,9vw,68px)] font-semibold leading-[1.02] tracking-[-0.035em]">
            Your cross-platform playtime, <span className="text-accent">perfectly organized.</span>
          </h1>
          <p className="m-0 max-w-[34em] text-pretty text-[17px] leading-[1.55] text-ink-2 sm:text-[19px]">
            Connect your gaming accounts and keep a living history of your sessions, streaks, and favorite titles across
            platforms.
          </p>
          <PlatformChips />
          <div className="flex flex-wrap items-center gap-4">
            <a href={dashUrl} className="pdx-btn-primary !h-[52px] !px-7 !text-[15px] no-underline">
              Open Playdex →
            </a>
            <span className="text-[13px] text-muted">Invite-only for now. Ask an admin for access.</span>
          </div>
        </section>

        <section className="flex flex-col gap-5">
          <div className="px-2">
            <p className="pdx-kicker">Features</p>
            <h2 className="mb-1.5 mt-2 text-[32px] font-semibold tracking-[-0.01em]">Every hour you play, accounted for</h2>
            <p className="m-0 text-[15px] text-muted">
              Automatic where it can be, effortless where it can't. One history across every platform you own.
            </p>
          </div>
          <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(260px,100%),1fr))]">
            {features.map((feature) => (
              <div key={feature.title} className="pdx-card flex flex-col gap-4 !rounded-[28px] !p-7">
                <span className="grid h-[52px] w-[52px] place-items-center rounded-2xl bg-mint text-accent">{feature.icon}</span>
                <h3 className="m-0 text-lg font-semibold leading-[1.3]">{feature.title}</h3>
                <p className="m-0 text-sm leading-normal text-muted">{feature.body}</p>
              </div>
            ))}
          </div>
        </section>

        <section aria-labelledby="launcher-teaser">
          <Link
            to="/launcher"
            className="pdx-card group grid items-center gap-7 text-inherit no-underline transition-[border-color,transform] duration-200 hover:-translate-y-0.5 hover:border-mint-hover focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-accent motion-reduce:transition-none motion-reduce:hover:translate-y-0 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)]"
          >
            <div className="flex flex-col gap-4">
              <p className="pdx-kicker">New · Playdex Launcher</p>
              <h2 id="launcher-teaser" className="m-0 text-[32px] font-semibold leading-[1.1] tracking-[-0.01em]">
                A console home screen for your Windows PC
              </h2>
              <p className="m-0 text-[15px] leading-relaxed text-muted">
                Boot into your library instead of the desktop. The launcher replaces Steam Big Picture in Windows Full Screen
                Experience, opens on the game you were playing and records every session to Playdex on its own.
              </p>
              <span className="mt-1 inline-flex items-center gap-1.5 text-[15px] font-semibold text-accent">
                See the launcher
                <span aria-hidden="true" className="transition-transform duration-200 group-hover:translate-x-1 motion-reduce:transition-none">
                  →
                </span>
              </span>
            </div>
            <img
              src="/launcher/home.webp"
              alt="The Playdex Launcher home screen: the last played game leads, with Playdex stats on the right"
              width={1920}
              height={1080}
              loading="lazy"
              decoding="async"
              className="aspect-video w-full rounded-[22px] bg-tile object-cover shadow-cover"
            />
          </Link>
        </section>

        <section className="pdx-card flex flex-col gap-7">
          <CardHeading
            kicker="Sessions recap · Preview"
            title="A month at a glance"
            actions={
              <span className="rounded-full bg-tile px-3.5 py-1.5 text-xs font-semibold text-ink-2">Sample data</span>
            }
          />
          <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(200px,100%),1fr))]">
            {previewKpis.map((kpi) => (
              <StatTile key={kpi.label} label={kpi.label} value={kpi.value} />
            ))}
          </div>
          <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(320px,100%),1fr))]">
            <div className="pdx-tile flex flex-col gap-6 !p-7">
              <p className="pdx-label">Platform distribution</p>
              <div className="flex flex-wrap items-center gap-8">
                <Donut
                  segments={donutSegments}
                  center={
                    <div className="flex flex-col items-center gap-0.5 leading-[1.1]">
                      <span className="pdx-num text-xl font-semibold">{previewTotalHours}</span>
                      <span className="text-xs text-muted">total</span>
                    </div>
                  }
                />
                <div className="flex min-w-0 flex-[1_1_140px] flex-col gap-3.5">
                  {previewPlatforms.map((platform, index) => (
                    <LegendRow
                      key={platform.key}
                      color={donutSegments[index].color}
                      name={platform.label}
                      pct={`${platform.value}%`}
                      detail={platform.hours}
                    />
                  ))}
                </div>
              </div>
            </div>
            <div className="pdx-tile flex flex-col gap-3.5 !p-7">
              <div className="flex flex-wrap items-baseline justify-between gap-2">
                <span className="pdx-label">Hours by title</span>
                <span className="pdx-num text-[13px] text-muted">
                  Peak {peakGame.title} · {peakGame.hours}h
                </span>
              </div>
              <BarChart data={topGameBars} height={150} gap={10} radius={6} ariaLabel="Hours by title" />
            </div>
          </div>
        </section>

        <section className="pdx-card flex flex-col gap-6">
          <CardHeading
            kicker="Most popular"
            title="Top 5 games from recap"
            actions={<span className="text-xs font-semibold uppercase tracking-[0.2em] text-muted">Jan 1 – Dec 31</span>}
          />
          <ol className="m-0 grid list-none gap-4 p-0 [grid-template-columns:repeat(auto-fit,minmax(min(120px,100%),1fr))]">
            {previewTopGames.map((game, index) => (
              <li key={game.gameId} className="flex min-w-0 max-w-[240px] flex-col gap-3">
                <div className="relative">
                  <Cover
                    title={game.title}
                    src={game.cover}
                    className="aspect-[2/3] w-full rounded-[22px] shadow-cover"
                    style={{ background: coverFallbacks[index % coverFallbacks.length] }}
                    initialClassName="text-[40px]"
                  />
                  <span className="absolute left-2.5 top-2.5 grid h-8 min-w-8 place-items-center rounded-full bg-white/90 px-2.5 text-[13px] font-bold text-pill-ink">
                    <span className="sr-only">Rank </span>
                    {index + 1}
                  </span>
                </div>
                <div className="flex min-w-0 flex-col gap-0.5">
                  <span title={game.title} className="line-clamp-2 break-words text-[15px] font-medium leading-[1.3]">
                    {game.title}
                  </span>
                  <span className="pdx-num text-sm font-semibold text-accent">{formatHoursShort(game.hours * 3600)}</span>
                </div>
              </li>
            ))}
          </ol>
        </section>

        <AppFooter items={[]} />
      </div>
    </main>
  );
}
