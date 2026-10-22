import {
  useId,
  useRef,
  type ButtonHTMLAttributes,
  type InputHTMLAttributes,
  type KeyboardEvent,
  type ReactNode,
  type SelectHTMLAttributes,
} from "react";
import { CardHeading } from "@/components/ui";
import { cx } from "@/lib/cx";
import { dialogKeyDown, useDialogFocus } from "@/lib/focusTrap";

type Variant = "primary" | "secondary" | "accent" | "danger";

const variants: Record<Variant, string> = {
  primary: "pdx-btn-primary",
  secondary: "pdx-btn-outline",
  accent: "pdx-btn-accent",
  danger: "pdx-btn-danger",
};

/** Pill button over the shared pdx-btn-* styles; `size="sm"` is the compact table-row size. */
export function Button({
  variant = "secondary",
  size = "md",
  className,
  type = "button",
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; size?: "sm" | "md" }) {
  return (
    <button
      type={type}
      className={cx(variants[variant], size === "sm" && "!h-[34px] !px-4 !text-[13px]", className)}
      {...props}
    />
  );
}

/** A page section: frosted card with a kicker, title and optional actions. */
export function Panel({
  kicker,
  title,
  actions,
  label,
  className,
  children,
}: {
  kicker: ReactNode;
  title?: ReactNode;
  actions?: ReactNode;
  /** Accessible name for the section landmark. */
  label?: string;
  className?: string;
  children?: ReactNode;
}) {
  return (
    <section aria-label={label} className={cx("pdx-card flex flex-col gap-5", className)}>
      <CardHeading kicker={kicker} title={title} actions={actions} />
      {children}
    </section>
  );
}

/** Short muted paragraph under a card heading. */
export function Note({ children, className }: { children: ReactNode; className?: string }) {
  return <p className={cx("text-sm text-muted", className)}>{children}</p>;
}

export const linkClass = "pdx-hit font-semibold text-accent underline-offset-4 hover:underline";

export function Alert({ tone = "info", children }: { tone?: "info" | "danger"; children: ReactNode }) {
  return tone === "danger" ? (
    <div role="alert" className="rounded-[18px] border border-danger-line bg-danger-tint px-4 py-3 text-sm text-danger">
      {children}
    </div>
  ) : (
    <div role="status" className="pdx-dashed">
      {children}
    </div>
  );
}

export function ProgressBar() {
  return (
    <div role="progressbar" aria-label="Loading" className="h-1.5 w-full overflow-hidden rounded-full bg-track">
      <div className="h-full w-1/3 rounded-full bg-bar motion-safe:animate-pulse" />
    </div>
  );
}

export function Card({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cx("pdx-card", className)}>{children}</div>;
}

export function TextInput({ className, ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return <input type="text" className={cx("pdx-input", className)} {...props} />;
}

export function Select({ className, ...props }: SelectHTMLAttributes<HTMLSelectElement>) {
  return <select className={cx("pdx-input", className)} {...props} />;
}

export function Field({ label, htmlFor, hint, children }: { label: string; htmlFor: string; hint?: ReactNode; children: ReactNode }) {
  return (
    <div className="flex min-w-0 flex-col gap-1.5">
      <label htmlFor={htmlFor} className="px-1 text-xs font-medium text-muted">
        {label}
      </label>
      {children}
      {hint}
    </div>
  );
}

export function Checkbox({ label, checked, onChange }: { label: string; checked: boolean; onChange: (checked: boolean) => void }) {
  return (
    <label className="pdx-hit inline-flex cursor-pointer items-center gap-2.5 text-sm font-medium text-ink-2">
      <input
        type="checkbox"
        className="h-4 w-4 cursor-pointer accent-brand"
        checked={checked}
        onChange={(event) => onChange(event.target.checked)}
      />
      {label}
    </label>
  );
}

export function Toolbar({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cx("flex flex-wrap items-center gap-2.5", className)}>{children}</div>;
}

/**
 * Dense data table. Wide tables scroll horizontally inside the card instead of the page; the scroll
 * region is focusable so keyboard users can scroll it too.
 */
export function Table({ children, label }: { children: ReactNode; label?: string }) {
  return (
    <div
      role={label ? "region" : undefined}
      aria-label={label}
      tabIndex={label ? 0 : undefined}
      className="-mx-2 overflow-x-auto rounded-[14px] px-2 outline-none focus-visible:ring-2 focus-visible:ring-bar"
    >
      <table className="w-full min-w-full border-collapse text-left text-sm [&_tbody_tr:last-child_td]:border-b-0">
        {children}
      </table>
    </div>
  );
}

export function Th({ children, className }: { children?: ReactNode; className?: string }) {
  return (
    <th scope="col" className={cx("pdx-label whitespace-nowrap border-b border-divider px-3 pb-3 pt-1 font-medium", className)}>
      {children}
    </th>
  );
}

export function Td({ children, className }: { children?: ReactNode; className?: string }) {
  return <td className={cx("border-b border-divider px-3 py-3 align-middle text-ink-2", className)}>{children}</td>;
}

export const rowClass = "transition-colors hover:bg-[var(--row-hover)]";

type BadgeTone = "success" | "danger" | "warn" | "neutral";

const badgeTones: Record<BadgeTone, string> = {
  success: "border-mint-line bg-mint text-accent",
  danger: "border-danger-line bg-danger-tint text-danger",
  warn: "border-line bg-tile text-warn",
  neutral: "border-line bg-tile text-muted",
};

/** Small status pill. */
export function Badge({ tone = "neutral", live = false, children }: { tone?: BadgeTone; live?: boolean; children: ReactNode }) {
  return (
    <span
      className={cx(
        "inline-flex items-center gap-1.5 whitespace-nowrap rounded-full border px-2.5 py-0.5 text-xs font-semibold",
        badgeTones[tone],
      )}
    >
      {live ? <span aria-hidden="true" className="pdx-live-dot" /> : null}
      {children}
    </span>
  );
}

/** Secondary line under a table cell value (account ids, emails). */
export function Sub({ children }: { children: ReactNode }) {
  return <span className="block text-xs text-muted">{children}</span>;
}

export function Pager({
  label,
  page,
  hasNext,
  disabled,
  onPage,
}: {
  label: ReactNode;
  page: number;
  hasNext: boolean;
  disabled?: boolean;
  onPage: (page: number) => void;
}) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 border-t border-divider pt-4 text-sm text-muted">
      <span className="pdx-num">{label}</span>
      <div className="flex gap-2">
        <Button size="sm" onClick={() => onPage(page - 1)} disabled={page === 0 || disabled}>
          Previous
        </Button>
        <Button size="sm" onClick={() => onPage(page + 1)} disabled={!hasNext || disabled}>
          Next
        </Button>
      </div>
    </div>
  );
}

/**
 * Centered dialog over a blurred backdrop. Focuses the first form control on open, keeps Tab inside,
 * closes on Escape or a backdrop click (unless busy) and returns focus to the opener on close.
 */
export function Modal({
  kicker,
  title,
  busy = false,
  onClose,
  children,
}: {
  kicker: ReactNode;
  title: ReactNode;
  busy?: boolean;
  onClose: () => void;
  children: ReactNode;
}) {
  const titleId = useId();
  const dialogRef = useRef<HTMLElement>(null);
  useDialogFocus(dialogRef);

  const onKeyDown = (event: KeyboardEvent) => dialogKeyDown(event, dialogRef.current, busy ? undefined : onClose);

  return (
    <div
      className="pdx-modal-backdrop fixed inset-0 z-[70] grid place-items-center overflow-y-auto p-4 sm:p-6"
      onClick={(event) => {
        if (event.target === event.currentTarget && !busy) onClose();
      }}
      onKeyDown={onKeyDown}
    >
      <section
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="pdx-modal flex w-[min(520px,100%)] flex-col gap-5 text-ink max-sm:!p-6"
      >
        <div className="flex items-start justify-between gap-4">
          <div className="min-w-0">
            <p className="pdx-kicker">{kicker}</p>
            <h2 id={titleId} className="mt-2 break-words text-[22px] font-semibold leading-tight">
              {title}
            </h2>
          </div>
          <button
            type="button"
            aria-label="Close"
            disabled={busy}
            onClick={onClose}
            className="pdx-icon-btn text-base disabled:cursor-not-allowed disabled:opacity-60"
          >
            ×
          </button>
        </div>
        {children}
      </section>
    </div>
  );
}
