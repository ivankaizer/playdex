import { useEffect, useState, type ReactNode } from "react";
import { Link } from "react-router";
import { AppFooter } from "@/components/AppFooter";
import { CardHeading } from "@/components/ui";
import { cx } from "@/lib/cx";
import { setupUrl } from "@/lib/downloads";
import { dashUrl } from "@/pages/landing/dashUrl";
import { LandingHeader } from "@/pages/landing/LandingHeader";

export { setupUrl };

/* Screenshots of the real launcher at 3840×2160, downscaled to 1920 in landing/public/launcher. */
const screenshot = (name: string) => `/launcher/${name}.webp`;

const tour: ReadonlyArray<{ key: string; label: string; body: string; alt: string }> = [
  {
    key: "home",
    label: "Home",
    body: "Opens on the game you played last. Continue is one press away.",
    alt: "The Playdex Launcher home screen: CONTROL Resonant leads as the last played game, recent games follow, and today's playtime, the week and the streak from Playdex sit on the right",
  },
  {
    key: "library",
    label: "Library",
    body: "Steam and local games together, with your hours.",
    alt: "The launcher library grid with All, Steam and Local tabs and hours played on each tile",
  },
  {
    key: "quick-settings",
    label: "Quick settings",
    body: "Sound, HDR, refresh rate and power, from the pad.",
    alt: "The quick settings panel with sound output, HDR, refresh rate, FPS counter and power buttons",
  },
];

const tourIntervalMs = 5000;

/** The launcher's screens as tabs over one screenshot. It steps on its own until someone picks a tab. */
function LauncherTour() {
  const [active, setActive] = useState(0);
  const [auto, setAuto] = useState(() => !window.matchMedia?.("(prefers-reduced-motion: reduce)").matches);

  useEffect(() => {
    if (!auto) return;
    const id = window.setInterval(() => setActive((index) => (index + 1) % tour.length), tourIntervalMs);
    return () => window.clearInterval(id);
  }, [auto]);

  const pick = (index: number) => {
    setAuto(false);
    setActive(index);
  };

  return (
    <section aria-label="A look at the launcher" className="flex flex-col gap-4">
      <div role="tablist" aria-label="Launcher screens" className="flex flex-wrap gap-2 px-2">
        {tour.map((screen, index) => (
          <button
            key={screen.key}
            type="button"
            role="tab"
            id={`tour-${screen.key}`}
            aria-selected={index === active}
            aria-controls="tour-panel"
            onClick={() => pick(index)}
            className={cx(
              "relative h-[38px] overflow-hidden rounded-full border px-[18px] text-[13px] font-semibold transition-colors",
              index === active
                ? "border-transparent bg-pill text-pill-ink"
                : "border-[color:var(--card-border)] bg-surface/75 text-ink-2 hover:text-accent",
            )}
          >
            {screen.label}
            {auto && index === active && (
              <span
                key={active}
                aria-hidden="true"
                className="pdx-tour-progress absolute inset-x-0 bottom-0 h-[3px] origin-left bg-bar"
                style={{ animationDuration: `${tourIntervalMs}ms` }}
              ></span>
            )}
          </button>
        ))}
      </div>
      <figure id="tour-panel" role="tabpanel" aria-labelledby={`tour-${tour[active].key}`} className="m-0 flex flex-col gap-3">
        <figcaption className="px-2 text-[17px] text-ink-2">{tour[active].body}</figcaption>
        <div className="relative aspect-video w-full overflow-hidden rounded-card bg-tile shadow-cover">
          {tour.map((screen, index) => (
            <img
              key={screen.key}
              src={screenshot(screen.key)}
              alt={index === active ? screen.alt : ""}
              aria-hidden={index === active ? undefined : true}
              width={1920}
              height={1080}
              fetchPriority={index === 0 ? "high" : undefined}
              decoding="async"
              className={cx(
                "absolute inset-0 h-full w-full object-cover transition-opacity duration-500 motion-reduce:transition-none",
                index === active ? "opacity-100" : "opacity-0",
              )}
            />
          ))}
        </div>
      </figure>
    </section>
  );
}

const installSteps: ReadonlyArray<{ title: string; body: ReactNode }> = [
  { title: "Download and run Playdex-Setup.exe", body: "It installs for your user and keeps itself updated." },
  { title: "Paste your server and API key", body: "Both are in Playdex under Settings → Integrations → Playdex app." },
  { title: "Make it your home screen", body: "Tick “Also use Playdex as the Full Screen Experience home” in setup." },
];

const requirements: readonly string[] = [
  "Windows 11, build 26100.7019 or later",
  "Full Screen Experience (ROG Ally, Legion Go or any PC with it)",
  "Developer Mode on, while it’s outside the Microsoft Store",
  "A Playdex account",
];

/** playdex.dev/launcher: the Playdex Launcher, a console-style home screen for Windows Full Screen Experience. */
export function LauncherPage() {
  useEffect(() => {
    window.scrollTo(0, 0);
    const previousTitle = document.title;
    document.title = "Playdex Launcher: a console home screen for Windows";
    return () => {
      document.title = previousTitle;
    };
  }, []);

  return (
    <main className="pdx-page relative px-4 pb-10 pt-6 sm:px-8 sm:pt-8">
      <div className="relative mx-auto flex w-full max-w-[1180px] flex-col gap-7">
        <LandingHeader />

        <section className="flex flex-col gap-7 px-2 pb-2 pt-6 sm:pt-10">
          <Link to="/" className="pdx-hit w-fit text-[13px] font-medium text-muted no-underline transition hover:text-accent">
            ← Playdex
          </Link>
          <p className="pdx-kicker">Playdex Launcher · Windows preview</p>
          <h1 className="m-0 max-w-[15em] text-[clamp(40px,9vw,68px)] font-semibold leading-[1.02] tracking-[-0.035em]">
            Your gaming PC, <span className="text-accent">as a console.</span>
          </h1>
          <p className="m-0 max-w-[36em] text-pretty text-[17px] leading-[1.55] text-ink-2 sm:text-[19px]">
            A controller-first home screen for Windows. Your games and your Playdex stats, no desktop.
          </p>
          <div className="flex flex-wrap items-center gap-4">
            <a href={setupUrl} className="pdx-btn-primary !h-[52px] !px-7 !text-[15px] no-underline">
              Download for Windows
            </a>
            <a href="#install" className="pdx-btn-outline !h-[52px] !px-7 !text-[15px] no-underline">
              How to install
            </a>
          </div>
        </section>

        <LauncherTour />

        <section id="install" className="grid scroll-mt-6 gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(340px,100%),1fr))]">
          <div className="pdx-card flex flex-col gap-6">
            <CardHeading kicker="Get started" title="Install in three steps" />
            <ol className="m-0 flex list-none flex-col gap-5 p-0">
              {installSteps.map((step, index) => (
                <li key={step.title} className="flex gap-4">
                  <span className="pdx-num grid h-9 w-9 shrink-0 place-items-center rounded-full bg-mint text-sm font-bold text-accent">
                    {index + 1}
                  </span>
                  <div className="flex flex-col gap-1">
                    <span className="text-[15px] font-semibold">{step.title}</span>
                    <span className="text-sm leading-normal text-muted">{step.body}</span>
                  </div>
                </li>
              ))}
            </ol>
            <a href={setupUrl} className="pdx-btn-primary w-fit !h-[46px] !px-6 no-underline">
              Download Playdex-Setup.exe
            </a>
          </div>
          <div className="pdx-card flex flex-col gap-6">
            <CardHeading kicker="Before you start" title="What you need" />
            <ul className="m-0 flex list-none flex-col gap-4 p-0">
              {requirements.map((requirement) => (
                <li key={requirement} className="flex gap-3 text-[15px] leading-normal text-ink-2">
                  <span aria-hidden="true" className="mt-[9px] h-2 w-2 shrink-0 rounded-full bg-bar"></span>
                  {requirement}
                </li>
              ))}
            </ul>
            <p className="m-0 rounded-tile bg-tile px-5 py-4 text-sm leading-normal text-muted">
              PlayStation, Xbox and Switch keep syncing through{" "}
              <a href={dashUrl} className="font-medium text-accent">
                Playdex
              </a>
              , so the stats cover everything.
            </p>
          </div>
        </section>

        <AppFooter items={[]} />
      </div>
    </main>
  );
}
