import { Link, NavLink, Outlet, useLocation, useNavigate } from "react-router";
import { PlaydexMark } from "@/components/AppFooter";
import { ThemeToggle } from "@/components/ThemeToggle";
import { cx } from "@/lib/cx";
import { useTheme } from "@/lib/theme";
import { useAdminEmail } from "./adminUser";
import { adminLogout } from "./auth";
import { userInitials } from "./logic";

const navigation = [
  { label: "Dashboard", to: "/admin", end: true },
  { label: "Integrations", to: "/admin/integrations", end: false },
  { label: "Sessions", to: "/admin/sessions", end: false },
  { label: "AI generations", to: "/admin/ai-generations", end: false },
  { label: "Games", to: "/admin/games", end: false },
] as const;

/** The header title for the current admin route. */
function pageTitle(pathname: string): string {
  const path = pathname.replace(/\/+$/, "") || "/admin";
  const match = navigation.find((item) => (item.end ? path === item.to : path === item.to || path.startsWith(`${item.to}/`)));
  return match?.label ?? "Dashboard";
}

const pillBase = "pdx-hit rounded-full border px-[18px] py-[8px] text-[13px] tracking-[0.04em] no-underline transition";

function AdminHeader() {
  const email = useAdminEmail();
  const location = useLocation();
  const navigate = useNavigate();
  const { theme, toggleTheme } = useTheme();

  const logout = () => {
    adminLogout();
    navigate("/admin/login", { replace: true });
  };

  return (
    <header className="pdx-card relative z-30 flex flex-wrap items-center gap-4 !rounded-[28px] !py-[18px] !pl-7 !pr-5">
      <div className="mr-auto min-w-0">
        <p className="text-[11px] font-semibold uppercase tracking-[0.32em] text-accent">
          Playdex <span className="text-muted">· Admin</span>
        </p>
        <h1 className="truncate text-2xl font-semibold tracking-[-0.01em]">{pageTitle(location.pathname)}</h1>
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <nav aria-label="Admin" className="flex flex-wrap gap-1.5">
          {navigation.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) =>
                cx(
                  pillBase,
                  isActive
                    ? "border-pill bg-pill font-semibold text-pill-ink"
                    : "border-line font-medium text-ink-2 hover:bg-surface",
                )
              }
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
        <div className="flex items-center gap-3">
          <ThemeToggle theme={theme} onToggle={toggleTheme} />
          <Link
            to="/dashboard"
            className="pdx-btn-outline !h-[38px] !px-4 !text-[13px] no-underline"
            title="Back to the Playdex app"
          >
            <svg aria-hidden="true" viewBox="0 0 24 24" className="h-3.5 w-3.5" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M15 18l-6-6 6-6" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
            Back to app
          </Link>
          <span
            className="grid h-[38px] w-[38px] shrink-0 place-items-center rounded-full bg-mint text-xs font-semibold text-accent"
            title={email}
          >
            {userInitials(email)}
            <span className="sr-only">Signed in as {email}</span>
          </span>
          <button
            type="button"
            aria-label="Logout"
            title="Log out"
            onClick={logout}
            className="pdx-icon-btn !border-danger-line text-danger-icon hover:!bg-danger-tint"
          >
            <svg aria-hidden="true" viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="1.9">
              <path d="M10 17l5-5-5-5M15 12H3M21 19V5a2 2 0 0 0-2-2h-6" strokeLinecap="round" strokeLinejoin="round"></path>
            </svg>
          </button>
        </div>
      </div>
    </header>
  );
}

/** Slim dark footer: the admin console is an operator surface, so it only links back out. */
function AdminFooter() {
  return (
    <footer className="flex flex-wrap items-center justify-between gap-x-6 gap-y-3 rounded-card bg-footer px-6 py-5 text-[13px] text-[#8a96a3] sm:px-9">
      <div className="flex items-center gap-3">
        <PlaydexMark />
        <span>Admin console</span>
      </div>
      <div className="flex flex-wrap gap-5">
        <Link to="/dashboard" className="pdx-hit text-[#b5c0cb] no-underline transition hover:text-[#6fe3b5]">
          Playdex app
        </Link>
        <Link to="/privacy" className="pdx-hit text-[#b5c0cb] no-underline transition hover:text-[#6fe3b5]">
          Privacy
        </Link>
      </div>
    </footer>
  );
}

export function AdminLayout() {
  return (
    <main className="pdx-page relative px-4 pb-10 pt-6 sm:px-8 sm:pt-12">
      <div className="relative mx-auto flex w-full max-w-[1180px] flex-col gap-7">
        <AdminHeader />
        <Outlet />
        <AdminFooter />
      </div>
    </main>
  );
}
