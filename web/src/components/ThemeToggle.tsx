type ThemeToggleProps = {
  theme: "light" | "dark";
  onToggle: () => void;
  className?: string;
};

export const ThemeToggle = ({
  theme,
  onToggle,
  className = "",
}: ThemeToggleProps) => {
  const nextTheme = theme === "dark" ? "light" : "dark";

  return (
    <button
      type="button"
      onClick={onToggle}
      aria-label={`Switch to ${nextTheme} mode`}
      aria-pressed={theme === "dark"}
      className={`relative inline-flex h-9 w-16 items-center justify-between rounded-full border border-slate-200 dark:border-slate-700 bg-white/80 dark:bg-slate-900/70 px-2 text-slate-600 dark:text-slate-200 shadow-sm transition ${className}`.trim()}
    >
      <span
        className={`absolute left-1 top-1 h-7 w-7 rounded-full bg-emerald-300 dark:bg-emerald-400 shadow-sm transition-transform ${
          theme === "dark" ? "translate-x-[26px]" : ""
        }`}
      />
      <span className="relative z-10 flex h-5 w-5 items-center justify-center text-amber-600 dark:text-amber-200">
        <svg
          aria-hidden="true"
          viewBox="0 0 24 24"
          className="h-4 w-4"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
        >
          <circle cx="12" cy="12" r="4" />
          <path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M4.93 19.07l1.41-1.41M17.66 6.34l1.41-1.41" />
        </svg>
      </span>
      <span className="relative z-10 flex h-5 w-5 items-center justify-center text-slate-500 dark:text-slate-100">
        <svg
          aria-hidden="true"
          viewBox="0 0 24 24"
          className="h-4 w-4"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
        >
          <path d="M21 12.8A9 9 0 1 1 11.2 3 7 7 0 0 0 21 12.8Z" />
        </svg>
      </span>
    </button>
  );
};
