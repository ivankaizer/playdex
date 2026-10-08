import { useRef, useState, type CSSProperties, type KeyboardEvent, type MouseEvent, type ReactNode } from "react";
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
  /** Extra hover text after the share, e.g. "64.9h". */
  detail?: string;
}

/** Rounded share of the total, as "52%" (or "<1%" for a sliver). */
function sharePercent(value: number, total: number): string {
  if (total <= 0) return "0%";
  const pct = (value / total) * 100;
  return pct > 0 && pct < 1 ? "<1%" : `${Math.round(pct)}%`;
}

function segmentHoverText(segment: DonutSegment, total: number): string {
  const share = sharePercent(segment.value, total);
  return segment.detail ? `${segment.label} · ${share} · ${segment.detail}` : `${segment.label} · ${share}`;
}

/** Small dark pill shown above a hovered chart element. */
function ChartTooltip({ text, style }: { text: string; style: CSSProperties }) {
  return (
    <span
      role="tooltip"
      className="pointer-events-none absolute z-20 -translate-x-1/2 -translate-y-full whitespace-nowrap rounded-full bg-footer px-3 py-1.5 text-xs font-semibold text-[#e9eef3] shadow-pop"
      style={style}
    >
      {text}
    </span>
  );
}

/** Conic-gradient donut with small gaps between segments, a centered label and per-segment hover. */
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
  const [hover, setHover] = useState<{ key: string; x: number; y: number } | null>(null);
  const total = segments.reduce((sum, s) => sum + Math.max(0, s.value), 0);
  const visible = segments.filter((s) => s.value > 0);
  const gap = visible.length > 1 ? 0.6 : 0;

  // Segment ranges in percent of the circle, clockwise from 12 o'clock (same order as the gradient).
  let cursor = 0;
  const ranges = visible.map((segment) => {
    const startPct = cursor;
    cursor += (segment.value / total) * 100;
    return { segment, start: startPct, end: cursor };
  });

  const stops: string[] = [];
  for (const { segment, start, end } of ranges) {
    const dimmed = hover !== null && hover.key !== segment.key;
    const color = dimmed ? `color-mix(in srgb, ${segment.color} 35%, transparent)` : segment.color;
    stops.push(`${color} ${start}% ${Math.max(start, end - gap)}%`);
    if (gap > 0) stops.push(`transparent ${Math.max(start, end - gap)}% ${end}%`);
  }
  const background = stops.length > 0 ? `conic-gradient(${stops.join(", ")})` : "rgb(var(--track))";

  const onMouseMove = (event: MouseEvent<HTMLDivElement>) => {
    const rect = event.currentTarget.getBoundingClientRect();
    const x = event.clientX - rect.left;
    const y = event.clientY - rect.top;
    const dx = x - rect.width / 2;
    const dy = y - rect.height / 2;
    const radius = Math.hypot(dx, dy);
    const outer = rect.width / 2;
    if (radius > outer || radius < outer - thickness) {
      setHover(null);
      return;
    }
    const angle = ((Math.atan2(dx, -dy) * 180) / Math.PI + 360) % 360;
    const pct = (angle / 360) * 100;
    const hit = ranges.find((range) => pct >= range.start && pct < range.end);
    setHover(hit ? { key: hit.segment.key, x, y } : null);
  };

  const hovered = hover ? visible.find((s) => s.key === hover.key) : undefined;
  return (
    <div
      role="img"
      aria-label={visible.map((s) => segmentHoverText(s, total)).join(", ") || "No data"}
      className="relative grid shrink-0 place-items-center rounded-full"
      style={{ width: size, height: size, background }}
      onMouseMove={onMouseMove}
      onMouseLeave={() => setHover(null)}
    >
      <div
        className={cx("grid place-items-center rounded-full text-center", holeClassName)}
        style={{ width: size - thickness * 2, height: size - thickness * 2 }}
      >
        {center}
      </div>
      {hover && hovered && (
        <ChartTooltip text={segmentHoverText(hovered, total)} style={{ left: hover.x, top: hover.y - 10 }} />
      )}
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

/** Vertical bar chart with labels under each bar and a tooltip per column on hover or focus. */
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
  const [active, setActive] = useState<string | null>(null);
  const [focusKey, setFocusKey] = useState<string | null>(null);
  const columnRefs = useRef(new Map<string, HTMLDivElement>());
  const max = Math.max(...data.map((d) => d.value), 0);
  const columns = { gridTemplateColumns: `repeat(${data.length}, minmax(0, 1fr))`, gap };
  const focusable = data.filter((d) => d.title);
  // One tab stop per chart (roving tabindex): the last focused bar, else the highlighted one, else the first.
  const tabKey =
    (focusKey !== null && focusable.some((d) => d.key === focusKey) ? focusKey : null) ??
    focusable.find((d) => d.highlight)?.key ??
    focusable[0]?.key;

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>, key: string) => {
    const index = focusable.findIndex((d) => d.key === key);
    const target =
      event.key === "ArrowRight" || event.key === "ArrowDown"
        ? focusable[Math.min(index + 1, focusable.length - 1)]
        : event.key === "ArrowLeft" || event.key === "ArrowUp"
          ? focusable[Math.max(index - 1, 0)]
          : event.key === "Home"
            ? focusable[0]
            : event.key === "End"
              ? focusable[focusable.length - 1]
              : undefined;
    if (!target) return;
    event.preventDefault();
    setFocusKey(target.key);
    columnRefs.current.get(target.key)?.focus();
  };

  return (
    <div aria-label={ariaLabel} role={ariaLabel ? "group" : undefined}>
      <div className="grid items-end" style={{ ...columns, height }} onMouseLeave={() => setActive(null)}>
        {data.map((d) => {
          const barHeight = max > 0 && d.value > 0 ? `max(${(d.value / max) * 100}%, ${minBar}px)` : `${minBar}px`;
          const isActive = active === d.key;
          return (
            // The whole column is the hover target, so short and empty bars are as easy to hover as tall ones.
            <div
              key={d.key}
              ref={(element) => {
                if (element) columnRefs.current.set(d.key, element);
                else columnRefs.current.delete(d.key);
              }}
              tabIndex={d.title ? (d.key === tabKey ? 0 : -1) : undefined}
              aria-label={d.title}
              onMouseEnter={() => setActive(d.key)}
              onFocus={() => {
                setActive(d.key);
                setFocusKey(d.key);
              }}
              onBlur={() => setActive((current) => (current === d.key ? null : current))}
              onKeyDown={d.title ? (event) => onKeyDown(event, d.key) : undefined}
              className="relative flex h-full items-end rounded-md outline-none focus-visible:ring-2 focus-visible:ring-bar"
            >
              <div
                className={cx(
                  "w-full transition-[filter,opacity]",
                  d.highlight ? "bg-brand" : "bg-bar-light",
                  d.faded && "opacity-35",
                  isActive && "brightness-110",
                  active !== null && !isActive && "opacity-60",
                )}
                style={{
                  height: barHeight,
                  borderRadius: radius,
                  opacity: !d.faded && d.value <= 0 && !isActive ? 0.35 : undefined,
                }}
              ></div>
              {isActive && d.title && (
                <ChartTooltip text={d.title} style={{ left: "50%", bottom: `calc(${barHeight} + 8px)`, transform: "translateX(-50%)" }} />
              )}
            </div>
          );
        })}
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

/** Horizontal stacked share bar with a tooltip per segment. */
export function StackedBar({ segments, height = 12 }: { segments: readonly DonutSegment[]; height?: number }) {
  const [active, setActive] = useState<string | null>(null);
  const visible = segments.filter((s) => s.value > 0);
  if (visible.length === 0) return <div className="rounded-full bg-track" style={{ height }}></div>;
  const total = visible.reduce((sum, s) => sum + s.value, 0);
  let cursor = 0;
  return (
    <div className="relative" onMouseLeave={() => setActive(null)}>
      <div
        role="img"
        aria-label={visible.map((s) => segmentHoverText(s, total)).join(", ")}
        className="flex gap-[3px] overflow-hidden rounded-full"
        style={{ height }}
      >
        {visible.map((s) => (
          <div
            key={s.key}
            onMouseEnter={() => setActive(s.key)}
            className="transition-opacity"
            style={{ flex: s.value, backgroundColor: s.color, opacity: active !== null && active !== s.key ? 0.4 : 1 }}
          ></div>
        ))}
      </div>
      {visible.map((s) => {
        const mid = cursor + (s.value / total) * 50;
        cursor += (s.value / total) * 100;
        return active === s.key ? (
          <ChartTooltip key={s.key} text={segmentHoverText(s, total)} style={{ left: `${mid}%`, top: -8 }} />
        ) : null;
      })}
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
