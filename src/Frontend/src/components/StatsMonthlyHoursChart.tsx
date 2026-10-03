import { formatMonthLabel, monthLabelsSpanYears } from "@/lib/dates";
import { formatHours } from "@/lib/format";
import type { AccountMonthStat } from "@/models/stats";

export function StatsMonthlyHoursChart({ months }: { months: readonly AccountMonthStat[] }) {
  if (months.length === 0) {
    return <p className="text-sm text-slate-500 dark:text-slate-400">No monthly data available.</p>;
  }

  const spansYears = monthLabelsSpanYears(months);
  const maxHours = Math.max(Math.max(...months.map((item) => item.totalSeconds / 3600)), 1);

  return (
    <div className="overflow-x-auto pb-2" aria-label="Hours per month">
      <div className="flex min-w-max items-end gap-3" role="list">
        {months.map((month) => {
          const label = formatMonthLabel(month, spansYears);
          const hoursLabel = formatHours(month.totalSeconds, 1);
          return (
            <div key={`${month.year}-${month.month}`} className="group relative flex w-12 shrink-0 flex-col gap-3" role="listitem">
              <div className="pointer-events-none absolute left-1/2 top-[-6px] z-10 -translate-x-1/2 -translate-y-full whitespace-nowrap rounded-md bg-white/95 px-3 py-1 text-[10px] font-semibold text-slate-900 opacity-0 shadow-lg ring-1 ring-slate-300/60 transition group-hover:opacity-100 group-focus-within:opacity-100 dark:bg-slate-900/95 dark:text-slate-100 dark:ring-slate-700/60">
                {hoursLabel}
              </div>
              <div className="flex h-32 items-end justify-center">
                <div
                  tabIndex={0}
                  aria-label={`${hoursLabel} played in ${label}`}
                  title={hoursLabel}
                  className="w-3 rounded-t-md bg-emerald-400/80 outline-none focus:ring-2 focus:ring-emerald-500"
                  style={{ height: `${(month.totalSeconds / 3600 / maxHours) * 100}%` }}
                ></div>
              </div>
              <div className="text-center text-[10px] uppercase tracking-[0.15em] text-slate-500 dark:text-slate-400">{label}</div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
