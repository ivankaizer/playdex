import { Link } from "react-router";
import { ThemeToggle } from "@/components/ThemeToggle";
import { useTheme } from "@/lib/theme";
import { dashUrl } from "./dashUrl";

/** The landing's top bar, shared by playdex.dev and its sub-pages. The wordmark leads back to the landing. */
export function LandingHeader() {
  const { theme, toggleTheme } = useTheme();

  return (
    <header className="flex flex-wrap items-center gap-4 rounded-[28px] border border-[color:var(--card-border)] bg-surface/60 py-3.5 pl-7 pr-4 sm:rounded-full">
      <Link to="/" aria-label="Playdex home" className="pdx-hit mr-auto flex items-center gap-2.5 no-underline">
        <span className="h-2.5 w-2.5 rounded-full bg-bar shadow-[0_0_0_4px_rgba(47,194,142,.18)]"></span>
        <span className="text-xs font-semibold uppercase tracking-[0.32em] text-accent">Playdex</span>
      </Link>
      <ThemeToggle theme={theme} onToggle={toggleTheme} />
      <a href={dashUrl} className="pdx-btn-primary !h-[38px] !px-[18px] !text-[13px] no-underline">
        Sign in
      </a>
    </header>
  );
}
