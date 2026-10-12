interface BreadcrumbsProps {
  parentLabel: string;
  currentLabel: string;
  onParent: () => void;
  disabled?: boolean;
}

export function Breadcrumbs({ parentLabel, currentLabel, onParent, disabled = false }: BreadcrumbsProps) {
  return (
    <nav aria-label="Breadcrumb" className="text-[13px] font-medium">
      <ol className="flex flex-wrap items-center gap-2">
        <li>
          <button
            type="button"
            disabled={disabled}
            onClick={onParent}
            className="text-muted transition hover:text-accent disabled:cursor-not-allowed disabled:opacity-60"
          >
            {parentLabel}
          </button>
        </li>
        <li aria-hidden="true" className="text-faint">
          ›
        </li>
        <li aria-current="page" className="text-[11px] font-semibold uppercase tracking-[0.24em] text-accent">
          {currentLabel}
        </li>
      </ol>
    </nav>
  );
}
