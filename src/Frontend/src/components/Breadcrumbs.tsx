interface BreadcrumbsProps {
  parentLabel: string;
  currentLabel: string;
  onParent: () => void;
  disabled?: boolean;
}

export function Breadcrumbs({ parentLabel, currentLabel, onParent, disabled = false }: BreadcrumbsProps) {
  return (
    <nav aria-label="Breadcrumb" className="text-xs font-semibold uppercase tracking-[0.16em]">
      <ol className="flex flex-wrap items-center gap-2">
        <li>
          <button
            type="button"
            disabled={disabled}
            onClick={onParent}
            className="text-slate-500 transition hover:text-emerald-700 disabled:cursor-not-allowed disabled:opacity-60 dark:text-slate-400 dark:hover:text-emerald-300"
          >
            {parentLabel}
          </button>
        </li>
        <li aria-hidden="true" className="text-slate-300 dark:text-slate-700">
          <svg viewBox="0 0 16 16" className="h-3 w-3" fill="none" stroke="currentColor" strokeWidth="1.8">
            <path d="m6 3 5 5-5 5" strokeLinecap="round" strokeLinejoin="round"></path>
          </svg>
        </li>
        <li aria-current="page" className="text-emerald-700 dark:text-emerald-300">
          {currentLabel}
        </li>
      </ol>
    </nav>
  );
}
