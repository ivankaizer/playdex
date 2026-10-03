import { useEffect, useState, type KeyboardEvent } from "react";
import { NavLink, Outlet, useLocation, useNavigate } from "react-router";
import { cx } from "@/lib/cx";
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

function Logo() {
  return (
    <svg width="34" height="34" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
      <rect width="24" height="24" rx="7" fill="#10b981" />
      <path d="M9.6 8.2v7.6l6.6-3.8-6.6-3.8Z" fill="#022c22" />
      <circle cx="8.2" cy="12" r="0.6" fill="#a7f3d0" />
      <circle cx="15.8" cy="12" r="0.6" fill="#a7f3d0" />
    </svg>
  );
}

export function AdminLayout() {
  const email = useAdminEmail();
  const location = useLocation();
  const navigate = useNavigate();
  const [menuOpen, setMenuOpen] = useState(false);

  // Close the mobile menu after every navigation.
  useEffect(() => setMenuOpen(false), [location.pathname, location.search]);

  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key === "Escape") setMenuOpen(false);
  };

  const logout = () => {
    adminLogout();
    navigate("/admin/login", { replace: true });
  };

  return (
    <div className="min-h-screen bg-slate-100 text-slate-900 dark:bg-slate-950 dark:text-slate-100">
      <header
        className="sticky top-0 z-30 flex items-center gap-3 border-b border-slate-200 bg-white px-4 py-2 md:hidden dark:border-slate-800 dark:bg-slate-900"
        onKeyDown={onKeyDown}
      >
        <button
          type="button"
          className="rounded p-1 text-slate-700 hover:bg-slate-100 dark:text-slate-200 dark:hover:bg-slate-800"
          aria-label="Toggle navigation"
          aria-controls="admin-navigation"
          aria-expanded={menuOpen ? "true" : "false"}
          onClick={() => setMenuOpen((open) => !open)}
        >
          <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
            {menuOpen ? <path d="m6 6 12 12M6 18 18 6" /> : <path d="M4 6h16M4 12h16M4 18h16" />}
          </svg>
        </button>
        <span className="font-semibold">
          Playdex <span className="text-emerald-600">Admin</span>
        </span>
      </header>
      {menuOpen ? (
        <button
          type="button"
          className="fixed inset-0 z-30 bg-black/40 md:hidden"
          aria-label="Close navigation"
          onClick={() => setMenuOpen(false)}
        />
      ) : null}
      <aside
        id="admin-navigation"
        onKeyDown={onKeyDown}
        className={cx(
          "fixed inset-y-0 left-0 z-40 flex w-60 flex-col border-r border-slate-200 bg-white transition-transform dark:border-slate-800 dark:bg-slate-900",
          menuOpen ? "translate-x-0" : "-translate-x-full md:translate-x-0",
        )}
      >
        <div className="flex items-center gap-3 px-5 py-5">
          <Logo />
          <div className="flex flex-col leading-tight">
            <span className="font-semibold">Playdex</span>
            <span className="text-xs uppercase tracking-wider text-slate-500 dark:text-slate-400">Admin</span>
          </div>
        </div>
        <nav className="flex flex-1 flex-col gap-1 px-3" aria-label="Admin">
          {navigation.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) =>
                cx(
                  "rounded-md px-3 py-2 text-sm font-medium",
                  isActive
                    ? "bg-emerald-50 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300"
                    : "text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800",
                )
              }
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
        <div className="border-t border-slate-200 p-3 dark:border-slate-800">
          <div className="mb-2 flex items-center gap-2 px-2">
            <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-emerald-600 text-xs font-semibold text-white">
              {userInitials(email)}
            </span>
            <span className="truncate text-sm text-slate-600 dark:text-slate-300" title={email}>
              {email}
            </span>
          </div>
          <button
            type="button"
            onClick={logout}
            className="w-full rounded-md px-3 py-2 text-left text-sm font-medium text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800"
          >
            Logout
          </button>
        </div>
      </aside>
      <main className="px-4 py-6 md:ml-60 md:px-8">
        <div className="mx-auto max-w-7xl">
          <Outlet />
        </div>
      </main>
    </div>
  );
}
