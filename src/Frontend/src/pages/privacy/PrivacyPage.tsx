import { useState, type ReactNode } from "react";
import { AppFooter } from "@/components/AppFooter";
import { AppLink } from "@/components/AppLink";
import { ThemeToggle } from "@/components/ThemeToggle";
import { authSession } from "@/lib/auth";
import { useTheme } from "@/lib/theme";

const LastUpdated = "October 5, 2026";

interface PolicySection {
  id: string;
  kicker: string;
  title: string;
  body: ReactNode;
}

const sections: readonly PolicySection[] = [
  {
    id: "what-we-store",
    kicker: "Your data",
    title: "What Playdex stores",
    body: (
      <ul>
        <li>
          <strong>Account:</strong> your email address and a bcrypt hash of your password. We never store the password itself.
          Accounts are invite-only; there is no public sign-up.
        </li>
        <li>
          <strong>Play history:</strong> each session's game, platform, start and end time, and how it was captured (synced,
          desktop app or logged by you), plus your game ratings, ignored games and connected games.
        </li>
        <li>
          <strong>Connected services:</strong> the account ID and the API key or token you give us for Steam (Web API key and
          SteamID), PlayStation (NPSSO token and account ID) or Xbox (OpenXBL key and XUID). We use them only to read your
          playtime. They are stored in our database <strong>without additional encryption</strong>, so use keys you can revoke.
        </li>
        <li>
          <strong>Sync responses:</strong> the raw responses Steam, PlayStation Network and OpenXBL return when we check your
          activity, kept for <strong>30 days</strong> to troubleshoot tracking and then deleted automatically.
        </li>
        <li>
          <strong>Settings:</strong> your short-session filter and, if you add one, your Telegram chat ID.
        </li>
      </ul>
    ),
  },
  {
    id: "ai-logging",
    kicker: "AI batch logging",
    title: "Text and screenshots you paste",
    body: (
      <>
        <p>
          When you use AI batch logging, the text you write (up to 10,000 characters) and any screenshot you attach are sent to
          our AI provider (OpenAI, or OpenRouter when configured) to turn them into draft sessions. Requests to OpenAI are made
          with storage disabled.
        </p>
        <p>
          We keep the text, the screenshot and the AI's response for <strong>30 days</strong> so administrators can investigate
          failed or wrong extractions, then delete them automatically. The resulting drafts (title, date, duration, platform)
          stay with your account.
        </p>
      </>
    ),
  },
  {
    id: "third-parties",
    kicker: "Sharing",
    title: "Services we send data to",
    body: (
      <>
        <p>We don't sell your data or use advertising or analytics trackers. We only send what each service needs:</p>
        <ul>
          <li>
            <strong>Steam, Sony PlayStation Network and OpenXBL (Xbox):</strong> your credentials and account ID, to read your
            activity.
          </li>
          <li>
            <strong>IGDB (Twitch):</strong> game search terms, to find titles and covers. No personal information is included.
          </li>
          <li>
            <strong>OpenAI or OpenRouter:</strong> the text and screenshots described above.
          </li>
          <li>
            <strong>Telegram</strong> (through our Apprise notification service), only if you add a chat ID: session summaries
            (game, platform, duration) and sync error alerts.
          </li>
          <li>
            <strong>Sentry:</strong> error reports and performance traces from the app and the API, with personal data
            collection turned off.
          </li>
          <li>
            <strong>Google Fonts and game artwork hosts (Steam, IGDB):</strong> your browser loads fonts and cover art
            from them, which shares your IP address with those providers.
          </li>
        </ul>
      </>
    ),
  },
  {
    id: "browser",
    kicker: "Your browser",
    title: "What we keep on your device",
    body: (
      <>
        <p>
          Playdex uses no cookies. It keeps four items in your browser's local storage: your sign-in token (valid 15 minutes),
          a refresh token (valid 30 days), your email address for display, and your light or dark theme. Logging out removes the
          tokens from your browser.
        </p>
        <p>
          The optional Windows desktop app checks locally which game is running and sends the game's title, ID and platform to
          Playdex while you play. It does not send your list of running programs.
        </p>
      </>
    ),
  },
  {
    id: "access",
    kicker: "Access",
    title: "Who can see your data",
    body: (
      <p>
        Only you, through your account, and Playdex administrators. Administrators can see account emails, sessions, connected
        service credentials and AI logging inputs in order to run the service and fix problems.
      </p>
    ),
  },
  {
    id: "control",
    kicker: "Your choices",
    title: "Changing or deleting your data",
    body: (
      <>
        <ul>
          <li>
            <strong>Download your data</strong> from Settings → Your data: a JSON file with everything above except the
            connected service keys themselves.
          </li>
          <li>
            <strong>Delete your account</strong> from Settings → Your data. After you confirm with your password, your account
            and everything tied to it (sessions, connected services and their keys, sync responses, settings, ratings, drafts
            and AI history) are permanently deleted at once.
          </li>
          <li>Remove any session from the Sessions page; it is permanently deleted.</li>
          <li>Pause a connected service, or clear its key, from Settings.</li>
          <li>Clear your Telegram chat ID in Settings to stop notifications and delete it.</li>
          <li>Remove ratings, ignored games and game connections at any time.</li>
        </ul>
        <p>
          Error reports already sent to Sentry, which are collected with personal data collection turned off, are not removed
          by account deletion and expire under Sentry's own retention. For anything else, use the Contact link below.
        </p>
      </>
    ),
  },
];

export function PrivacyPage() {
  const { theme, toggleTheme } = useTheme();
  const [isLoggedIn] = useState(() => authSession.isLoggedIn());

  return (
    <main className="pdx-page relative px-4 pb-10 pt-6 sm:px-8 sm:pt-8">
      <div className="relative mx-auto flex w-full max-w-[1180px] flex-col gap-7">
        <header className="flex flex-wrap items-center gap-4 rounded-[28px] border border-[color:var(--card-border)] bg-surface/60 py-3.5 pl-7 pr-4 sm:rounded-full">
          <AppLink to="/" className="mr-auto flex items-center gap-2.5 no-underline">
            <span className="h-2.5 w-2.5 rounded-full bg-bar shadow-[0_0_0_4px_rgba(47,194,142,.18)]"></span>
            <span className="text-xs font-semibold uppercase tracking-[0.32em] text-accent">Playdex</span>
          </AppLink>
          <ThemeToggle theme={theme} onToggle={toggleTheme} />
          <AppLink to={isLoggedIn ? "/dashboard" : "/"} className="pdx-btn-primary !h-[38px] !px-[18px] !text-[13px] no-underline">
            {isLoggedIn ? "Open dashboard →" : "Sign in"}
          </AppLink>
        </header>

        <section className="px-2 pt-4 sm:pt-8">
          <p className="pdx-kicker">Privacy</p>
          <h1 className="m-0 mt-3 text-[40px] font-semibold leading-[1.05] tracking-[-0.03em] sm:text-[56px]">
            Your data, plainly explained
          </h1>
          <p className="m-0 mt-4 max-w-[40em] text-[17px] leading-[1.55] text-ink-2">
            Playdex tracks your playtime. Here is exactly what that involves: what we store, who we share it with and how to
            remove it.
          </p>
          <p className="m-0 mt-3 text-[13px] text-muted">Last updated {LastUpdated}</p>
        </section>

        <div className="grid items-start gap-7 lg:grid-cols-[220px_minmax(0,1fr)]">
          <nav aria-label="On this page" className="pdx-card !p-6 lg:sticky lg:top-6">
            <p className="pdx-label">On this page</p>
            <ol className="m-0 mt-3 flex list-none flex-col gap-1 p-0">
              {sections.map((section) => (
                <li key={section.id}>
                  <a
                    href={`#${section.id}`}
                    className="pdx-row pdx-hit block px-3 py-2 text-sm text-ink-2 no-underline hover:text-accent"
                  >
                    {section.title}
                  </a>
                </li>
              ))}
            </ol>
          </nav>

          <div className="flex min-w-0 flex-col gap-7">
            {sections.map((section) => (
              <section key={section.id} id={section.id} aria-labelledby={`${section.id}-title`} className="pdx-card scroll-mt-6">
                <p className="pdx-kicker">{section.kicker}</p>
                <h2 id={`${section.id}-title`} className="pdx-title">
                  {section.title}
                </h2>
                <div className="mt-5 max-w-[46em] text-[15px] leading-[1.65] text-ink-2 [&_li]:mt-2.5 [&_p+p]:mt-4 [&_p]:m-0 [&_strong]:font-semibold [&_strong]:text-ink [&_ul+p]:mt-4 [&_ul]:m-0 [&_ul]:list-disc [&_ul]:pl-5 [&_ul]:marker:text-bar">
                  {section.body}
                </div>
              </section>
            ))}
          </div>
        </div>

        <AppFooter />
      </div>
    </main>
  );
}
