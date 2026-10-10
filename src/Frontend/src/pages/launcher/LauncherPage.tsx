import { useEffect, type ReactNode } from "react";
import { Link } from "react-router";
import { AppFooter } from "@/components/AppFooter";
import { CardHeading } from "@/components/ui";
import { dashUrl } from "@/pages/landing/dashUrl";
import { LandingHeader } from "@/pages/landing/LandingHeader";

/** The latest Playdex-Setup.exe on the public download feed (R2, see docs/windows-releases.md). It installs the desktop app,
 * which installs the launcher. */
export const setupUrl = "https://download.playdex.dev/windows/Playdex-Setup.exe";

/* Screenshots of the real launcher at 3840×2160, downscaled to 1920 in landing/public/launcher. */
const screenshot = (name: string) => `/launcher/${name}.webp`;

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
    title: "Jump back in",
    body: "Home opens on the game you played last, with Continue one press away. Everything else you play follows in a row.",
    icon: (
      <svg {...iconProps}>
        <path d="M7 4v16l13-8z"></path>
      </svg>
    ),
  },
  {
    title: "One library on this PC",
    body: "Your installed Steam games and the local games you added to Playdex, side by side, with hours played from your history.",
    icon: (
      <svg {...iconProps}>
        <rect x="3" y="4" width="7" height="7" rx="1.5"></rect>
        <rect x="14" y="4" width="7" height="7" rx="1.5"></rect>
        <rect x="3" y="15" width="7" height="5" rx="1.5"></rect>
        <rect x="14" y="15" width="7" height="5" rx="1.5"></rect>
      </svg>
    ),
  },
  {
    title: "Every session, recorded",
    body: "Games started from the launcher are tracked and sent to Playdex on their own, and queued on disk while you're offline.",
    icon: (
      <svg {...iconProps}>
        <circle cx="12" cy="13" r="8"></circle>
        <path d="M12 9v4l2.5 1.5M10 2h4"></path>
      </svg>
    ),
  },
  {
    title: "Settings without the desktop",
    body: "Switch sound output, HDR and refresh rate, toggle the NVIDIA FPS counter, or sleep and shut down, all from the pad.",
    icon: (
      <svg {...iconProps}>
        <path d="M4 7h10M18 7h2M4 17h4M12 17h8"></path>
        <circle cx="16" cy="7" r="2"></circle>
        <circle cx="10" cy="17" r="2"></circle>
      </svg>
    ),
  },
];

const gallery: ReadonlyArray<{ src: string; title: string; body: string; alt: string }> = [
  {
    src: screenshot("now-playing"),
    title: "Now playing",
    body: "A game started anywhere shows in the top bar, and Resume takes you straight back to it.",
    alt: "The launcher's top bar with a Playing pill for the running game and a Resume action",
  },
  {
    src: screenshot("library"),
    title: "Library",
    body: "All, Steam and Local tabs, each tile with the hours you've played it.",
    alt: "The launcher library grid with All, Steam and Local tabs and hours played on each tile",
  },
  {
    src: screenshot("quick-settings"),
    title: "Quick settings",
    body: "Sound output, HDR, refresh rate, FPS counter and power in one panel.",
    alt: "The quick settings panel with sound output, HDR, refresh rate, FPS counter and power buttons",
  },
];

const installSteps: ReadonlyArray<{ title: string; body: string }> = [
  {
    title: "Install Playdex for Windows",
    body: "Download and run Playdex-Setup.exe. It installs the desktop app for your user and starts it.",
  },
  {
    title: "Connect it to your account",
    body: "Add your Playdex API key in the desktop app's Settings, so sessions land in your history and the launcher can show it.",
  },
  {
    title: "Make it your home screen",
    body: "In the desktop app, choose “Use as Full Screen Experience home”. It trusts the launcher's certificate (one admin prompt) and installs it.",
  },
];

const requirements: readonly string[] = [
  "Windows 11, build 10.0.26100.7019 or later",
  "Full Screen Experience (handhelds such as the ROG Ally or Legion Go, or any PC that has it)",
  "Developer Mode turned on, while the launcher is outside the Microsoft Store",
  "A Playdex account (invite-only for now)",
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
            A controller-first home screen for Windows Full Screen Experience. It takes the place of Steam Big Picture, opens on
            what you were playing and keeps your Playdex history on screen, across every platform you play on.
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

        <figure className="m-0">
          <img
            src={screenshot("home")}
            alt="The Playdex Launcher home screen: CONTROL Resonant leads as the last played game, recent games follow, and today's playtime, the week and the streak from Playdex sit on the right"
            width={1920}
            height={1080}
            fetchPriority="high"
            className="aspect-video w-full rounded-card bg-tile object-cover shadow-cover"
          />
          <figcaption className="mt-3 px-2 text-[13px] text-muted">
            Home on a 4K TV: the last played game leads, and the rail on the right is your Playdex history.
          </figcaption>
        </figure>

        <section className="flex flex-col gap-5">
          <div className="px-2">
            <p className="pdx-kicker">Why switch</p>
            <h2 className="mb-1.5 mt-2 text-[32px] font-semibold tracking-[-0.01em]">Power on, pick up the pad, play</h2>
            <p className="m-0 text-[15px] text-muted">
              Your games, not a store. Built for the couch and the handheld, and just as happy with a mouse.
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

        <section className="pdx-card flex flex-col gap-6">
          <CardHeading kicker="A closer look" title="Made for ten feet away" />
          <div className="grid gap-5 [grid-template-columns:repeat(auto-fit,minmax(min(300px,100%),1fr))]">
            {gallery.map((shot) => (
              <figure key={shot.title} className="m-0 flex flex-col gap-3">
                <img
                  src={shot.src}
                  alt={shot.alt}
                  width={1920}
                  height={1080}
                  loading="lazy"
                  decoding="async"
                  className="aspect-video w-full rounded-[22px] bg-tile object-cover shadow-cover"
                />
                <figcaption className="flex flex-col gap-1 px-1">
                  <span className="text-[15px] font-semibold">{shot.title}</span>
                  <span className="text-sm leading-normal text-muted">{shot.body}</span>
                </figcaption>
              </figure>
            ))}
          </div>
        </section>

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
              The launcher is a preview. Playtime on PlayStation, Xbox and Switch still syncs through{" "}
              <a href={dashUrl} className="font-medium text-accent">
                Playdex
              </a>
              , so the rail covers everything, not just this PC.
            </p>
          </div>
        </section>

        <AppFooter items={[]} />
      </div>
    </main>
  );
}
