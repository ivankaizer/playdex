import { AppLink } from "./AppLink";

interface BreadcrumbsProps {
  parentLabel: string;
  /** The parent page's path. */
  parentTo: string;
  currentLabel: string;
}

export function Breadcrumbs({ parentLabel, parentTo, currentLabel }: BreadcrumbsProps) {
  return (
    <nav aria-label="Breadcrumb" className="text-[13px] font-medium">
      <ol className="flex flex-wrap items-center gap-2">
        <li>
          <AppLink to={parentTo} className="text-muted no-underline transition hover:text-accent">
            {parentLabel}
          </AppLink>
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
