import type { KeyboardEvent } from "react";

export interface PieSegment {
  key: string;
  label: string;
  value: number;
}

interface StatsPieChartProps {
  segments: readonly PieSegment[];
  colors: readonly string[];
  size?: number;
  selectedKey?: string;
  onSegmentSelect?: (key: string) => void;
}

const strokeWidth = 18;

export function StatsPieChart({ segments, colors, size = 150, selectedKey = "", onSegmentSelect }: StatsPieChartProps) {
  const radius = (size - strokeWidth) / 2;
  const circumference = 2 * Math.PI * radius;
  const total = Math.max(
    segments.reduce((sum, segment) => sum + segment.value, 0),
    1,
  );
  const center = size / 2;
  const selectable = onSegmentSelect !== undefined;
  let offset = 0;

  const handleKeyDown = (event: KeyboardEvent<SVGCircleElement>, key: string) => {
    if (!selectable || (event.key !== "Enter" && event.key !== " ")) return;
    event.preventDefault();
    onSegmentSelect(key);
  };

  return (
    <svg role="group" aria-label="Platform distribution" width={size} height={size} viewBox={`0 0 ${size} ${size}`} className="shrink-0">
      <circle
        cx={String(center)}
        cy={String(center)}
        r={String(radius)}
        fill="transparent"
        stroke="currentColor"
        strokeWidth={String(strokeWidth)}
        aria-hidden="true"
        className="text-slate-200 dark:text-slate-800"
      ></circle>
      {segments.map((segment, index) => {
        const dash = (segment.value / total) * circumference;
        const gap = circumference - dash;
        const strokeDashOffset = -offset;
        offset += dash;
        return (
          <circle
            key={index}
            cx={String(center)}
            cy={String(center)}
            r={String(radius)}
            fill="transparent"
            stroke={colors.length === 0 ? undefined : colors[index % colors.length]}
            strokeWidth={String(selectedKey === segment.key ? strokeWidth + 4 : strokeWidth)}
            strokeDasharray={`${dash} ${gap}`}
            strokeDashoffset={String(strokeDashOffset === 0 ? 0 : strokeDashOffset)}
            opacity={selectedKey.length > 0 && selectedKey !== segment.key ? "0.35" : "1"}
            role={selectable ? "button" : undefined}
            tabIndex={selectable ? 0 : undefined}
            aria-label={`Filter by ${segment.label}: ${segment.value} percent`}
            aria-pressed={selectable ? (selectedKey === segment.key ? "true" : "false") : undefined}
            onClick={() => onSegmentSelect?.(segment.key)}
            onKeyDown={(event) => handleKeyDown(event, segment.key)}
            transform={`rotate(-90 ${center} ${center})`}
            className={selectable ? "cursor-pointer outline-none focus:opacity-80" : undefined}
          ></circle>
        );
      })}
    </svg>
  );
}
