type PieSegment = {
  label: string;
  value: number;
};

type StatsPieChartProps = {
  segments: PieSegment[];
  colors: string[];
  size?: number;
};

export const StatsPieChart = ({
  segments,
  colors,
  size = 150,
}: StatsPieChartProps) => {
  const strokeWidth = 18;
  const radius = (size - strokeWidth) / 2;
  const circumference = 2 * Math.PI * radius;
  const total = Math.max(
    segments.reduce((sum, segment) => sum + segment.value, 0),
    1
  );
  let offset = 0;

  return (
    <svg
      aria-hidden="true"
      width={size}
      height={size}
      viewBox={`0 0 ${size} ${size}`}
      className="shrink-0"
    >
      <circle
        cx={size / 2}
        cy={size / 2}
        r={radius}
        fill="transparent"
        stroke="currentColor"
        strokeWidth={strokeWidth}
        className="text-slate-200 dark:text-slate-800"
      />
      {segments.map((segment, index) => {
        const dash = (segment.value / total) * circumference;
        const gap = circumference - dash;
        const strokeDasharray = `${dash} ${gap}`;
        const strokeDashoffset = -offset;
        offset += dash;
        return (
          <circle
            key={segment.label}
            cx={size / 2}
            cy={size / 2}
            r={radius}
            fill="transparent"
            stroke={colors[index % colors.length]}
            strokeWidth={strokeWidth}
            strokeDasharray={strokeDasharray}
            strokeDashoffset={strokeDashoffset}
            transform={`rotate(-90 ${size / 2} ${size / 2})`}
          />
        );
      })}
    </svg>
  );
};
