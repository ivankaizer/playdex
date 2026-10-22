import { navItems } from "@/lib/navigation";
import { AppLink } from "./AppLink";

export function PlaydexMark({ className = "" }: { className?: string }) {
  return (
    <div className={`flex items-center gap-2.5 ${className}`.trim()}>
      <span className="h-2.5 w-2.5 rounded-full bg-[#6fe3b5] shadow-[0_0_0_4px_rgba(111,227,181,.18)]"></span>
      <span className="text-xs font-semibold uppercase tracking-[0.32em] text-[#6fe3b5]">Playdex</span>
    </div>
  );
}

export function AppFooter({ items = navItems }: { items?: ReadonlyArray<{ label: string; path: string }> }) {
  return (
    <footer className="flex flex-col gap-6 rounded-card bg-footer px-6 py-7 text-[#e9eef3] sm:px-9">
      <div className="flex flex-wrap items-center justify-between gap-6">
        <PlaydexMark />
        {items.length > 0 && (
          <nav aria-label="Footer" className="flex flex-wrap gap-1.5">
            {items.map((item) => (
              <AppLink
                key={item.path}
                to={item.path}
                className="pdx-hit rounded-full border border-[#2f3b47] px-4 py-2 text-[13px] font-medium text-[#e9eef3] no-underline transition hover:bg-[#24303b] hover:text-white"
              >
                {item.label}
              </AppLink>
            ))}
          </nav>
        )}
      </div>
      <div className="flex flex-wrap items-center justify-between gap-x-6 gap-y-3 border-t border-[#2a3540] pt-[18px] text-[13px] text-[#8a96a3]">
        <span>© {new Date().getFullYear()} Playdex</span>
        <div className="flex flex-wrap gap-5">
          <AppLink to="/privacy" className="pdx-hit text-[#b5c0cb] no-underline transition hover:text-[#6fe3b5]">
            Privacy
          </AppLink>
          <a href="mailto:hello@playdex.dev" className="pdx-hit text-[#b5c0cb] no-underline transition hover:text-[#6fe3b5]">
            Contact
          </a>
          <a href="https://github.com/ivankaizer/playdex" target="_blank" rel="noreferrer" className="pdx-hit text-[#b5c0cb] no-underline transition hover:text-[#6fe3b5]">
            GitHub ↗
          </a>
        </div>
      </div>
    </footer>
  );
}
