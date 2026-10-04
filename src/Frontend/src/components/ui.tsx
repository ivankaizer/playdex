import { useState, type CSSProperties, type ReactNode } from "react";
import { cx } from "@/lib/cx";

/** Kicker over a card title, with optional right-side actions. */
export function CardHeading({
  kicker,
  title,
  actions,
  className,
}: {
  kicker: ReactNode;
  title?: ReactNode;
  actions?: ReactNode;
  className?: string;
}) {
  return (
    <div className={cx("flex flex-wrap items-end justify-between gap-4", className)}>
      <div className="min-w-0">
        <p className="pdx-kicker">{kicker}</p>
        {title !== undefined && <h2 className="pdx-title">{title}</h2>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-3">{actions}</div>}
    </div>
  );
}

/** Label tile with a big tabular value (KPI). */
export function StatTile({ label, value, sub, className }: { label: ReactNode; value: ReactNode; sub?: ReactNode; className?: string }) {
  return (
    <div className={cx("pdx-tile", className)}>
      <p className="pdx-label">{label}</p>
      <p className="pdx-num mt-2.5 truncate text-[36px] font-semibold leading-tight tracking-[-0.02em]">{value}</p>
      {sub !== undefined && <p className="mt-1 text-[13px] text-muted">{sub}</p>}
    </div>
  );
}

/** Colored platform dot. */
export function Dot({ color, size = 8, className }: { color: string; size?: number; className?: string }) {
  return (
    <span
      aria-hidden="true"
      className={cx("inline-block shrink-0 rounded-full", className)}
      style={{ width: size, height: size, backgroundColor: color }}
    ></span>
  );
}

/**
 * Game cover with a gradient + initial fallback when the image is missing or fails to load.
 * Size and radius come from className (e.g. "h-12 w-12 rounded-[14px]").
 */
export function Cover({
  title,
  src,
  className,
  style,
  initialClassName = "text-base",
}: {
  title: string;
  src?: string | null;
  className?: string;
  style?: CSSProperties;
  initialClassName?: string;
}) {
  const [failed, setFailed] = useState(false);
  const initial = title.trim().charAt(0).toUpperCase() || "?";
  const showImage = !!src && !failed;
  return (
    <div
      className={cx("relative grid shrink-0 place-items-center overflow-hidden font-semibold text-white", className)}
      style={{ background: "linear-gradient(135deg,#2b3540,#5a6672)", ...style }}
    >
      <span aria-hidden="true" className={initialClassName}>
        {initial}
      </span>
      {showImage && (
        <img
          src={src}
          alt=""
          loading="lazy"
          onError={() => setFailed(true)}
          className="absolute inset-0 h-full w-full object-cover"
        />
      )}
    </div>
  );
}

export interface DonutSegment {
  key: string;
  label: string;
  value: number;
  color: string;
}

/** Conic-gradient donut with small gaps between segments and a centered label. */
export function Donut({
  segments,
  size = 168,
  thickness = 20,
  center,
  holeClassName = "bg-tile",
}: {
  segments: readonly DonutSegment[];
  size?: number;
  thickness?: number;
  center?: ReactNode;
  holeClassName?: string;
}) {
  const total = segments.reduce((sum, s) => sum + Math.max(0, s.value), 0);
  const gap = segments.filter((s) => s.value > 0).length > 1 ? 0.6 : 0;
  let cursor = 0;
  const stops: string[] = [];
  if (total > 0) {
    for (const segment of segments) {
      if (segment.value <= 0) continue;
      const share = (segment.value / total) * 100;
      const end = cursor + share;
      stops.push(`${segment.color} ${cursor}% ${Math.max(cursor, end - gap)}%`);
      if (gap > 0) stops.push(`transparent ${Math.max(cursor, end - gap)}% ${end}%`);
      cursor = end;
    }
  }
  const background = stops.length > 0 ? `conic-gradient(${stops.join(", ")})` : "rgb(var(--track))";
  return (
    <div
      role="img"
      aria-label={segments.map((s) => `${s.label} ${s.value}`).join(", ") || "No data"}
      className="grid shrink-0 place-items-center rounded-full"
      style={{ width: size, height: size, background }}
    >
      <div
        className={cx("grid place-items-center rounded-full text-center", holeClassName)}
        style={{ width: size - thickness * 2, height: size - thickness * 2 }}
      >
        {center}
      </div>
    </div>
  );
}

/** Legend row: dot, name, bold percent · hours. */
export function LegendRow({ color, name, pct, detail }: { color: string; name: ReactNode; pct: ReactNode; detail?: ReactNode }) {
  return (
    <div className="grid grid-cols-[10px_minmax(0,1fr)_auto] items-center gap-2.5 text-sm">
      <Dot color={color} size={10} />
      <span className="truncate">{name}</span>
      <span className="pdx-num text-muted">
        <span className="font-semibold text-ink">{pct}</span>
        {detail !== undefined && <> · {detail}</>}
      </span>
    </div>
  );
}

export interface BarDatum {
  key: string;
  value: number;
  label: ReactNode;
  title?: string;
  /** Highlighted bars render in the primary green. */
  highlight?: boolean;
  /** Faded bars (e.g. future days). */
  faded?: boolean;
}

/** Vertical bar chart with labels under each bar. */
export function BarChart({
  data,
  height = 140,
  gap = 6,
  radius = 8,
  minBar = 4,
  ariaLabel,
}: {
  data: readonly BarDatum[];
  height?: number;
  gap?: number;
  radius?: number;
  minBar?: number;
  ariaLabel?: string;
}) {
  const max = Math.max(...data.map((d) => d.value), 0);
  const columns = { gridTemplateColumns: `repeat(${data.length}, minmax(0, 1fr))`, gap };
  return (
    <div aria-label={ariaLabel} role={ariaLabel ? "group" : undefined}>
      <div className="grid items-end" style={{ ...columns, height }}>
        {data.map((d) => (
          <div
            key={d.key}
            title={d.title}
            aria-label={d.title}
            className={cx(d.highlight ? "bg-brand" : "bg-bar-light", d.faded && "opacity-35")}
            style={{
              height: max > 0 && d.value > 0 ? `max(${(d.value / max) * 100}%, ${minBar}px)` : minBar,
              borderRadius: radius,
              opacity: !d.faded && d.value <= 0 ? 0.35 : undefined,
            }}
          ></div>
        ))}
      </div>
      <div className="mt-2.5 grid" style={columns}>
        {data.map((d) => (
          <span
            key={d.key}
            className={cx("pdx-num truncate text-center text-[11px]", d.highlight ? "font-semibold text-accent" : "text-muted")}
          >
            {d.label}
          </span>
        ))}
      </div>
    </div>
  );
}

/** Horizontal stacked share bar. */
export function StackedBar({ segments, height = 12 }: { segments: readonly DonutSegment[]; height?: number }) {
  const visible = segments.filter((s) => s.value > 0);
  if (visible.length === 0) return <div className="rounded-full bg-track" style={{ height }}></div>;
  return (
    <div className="flex gap-[3px] overflow-hidden rounded-full" style={{ height }}>
      {visible.map((s) => (
        <div key={s.key} title={`${s.label} · ${s.value}`} style={{ flex: s.value, backgroundColor: s.color }}></div>
      ))}
    </div>
  );
}

/** 6px progress bar used in top-game lists. */
export function ShareBar({ pct, className }: { pct: number; className?: string }) {
  return (
    <div className={cx("h-1.5 rounded-[3px] bg-track", className)}>
      <div className="h-1.5 rounded-[3px] bg-bar" style={{ width: `${Math.max(0, Math.min(100, pct))}%` }}></div>
    </div>
  );
}

/** Segmented switch. */
export function Segmented<T extends string>({
  options,
  value,
  onChange,
  size = "md",
  ariaLabel,
}: {
  options: ReadonlyArray<{ value: T; label: ReactNode }>;
  value: T;
  onChange: (value: T) => void;
  size?: "sm" | "md";
  ariaLabel?: string;
}) {
  return (
    <div className="pdx-seg" role="group" aria-label={ariaLabel}>
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          aria-pressed={option.value === value ? "true" : "false"}
          onClick={() => onChange(option.value)}
          className={cx("pdx-seg-btn", size === "md" && "!h-[38px] !px-5")}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}

/** Spinner-free loading placeholder line. */
export function LoadingNote({ children = "Loading…" }: { children?: ReactNode }) {
  return <p className="text-sm text-muted">{children}</p>;
}

/** Inline error banner. */
export function ErrorNote({ children }: { children: ReactNode }) {
  return (
    <p role="alert" className="rounded-[18px] border border-danger-line bg-danger-tint px-4 py-3 text-sm text-danger">
      {children}
    </p>
  );
}
