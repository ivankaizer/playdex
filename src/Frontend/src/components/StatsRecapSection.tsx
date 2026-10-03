import { StatsPieChart } from "./StatsPieChart";

export interface RecapSummaryItem {
  label: string;
  value: string;
}

export interface RecapPlatformItem {
  key: string;
  label: string;
  value: number;
  hours: string;
}

export interface RecapTopGame {
  gameId: string;
  title: string;
  hours: string;
}

interface StatsRecapSectionProps {
  title: string;
  subtitle: string;
  summary: readonly RecapSummaryItem[];
  platforms: readonly RecapPlatformItem[];
  topGames: readonly RecapTopGame[];
  colors: readonly string[];
  onGameSelect?: (gameId: string) => void;
  onOpenRecap?: () => void;
  selectedPlatform?: string;
  onPlatformSelect?: (key: string) => void;
}

export function StatsRecapSection({
  title,
  subtitle,
  summary,
  platforms,
  topGames,
  colors,
  onGameSelect,
  onOpenRecap,
  selectedPlatform = "",
  onPlatformSelect,
}: StatsRecapSectionProps) {
  const pieSegments = platforms.map((item) => ({ key: item.key, label: item.label, value: item.value }));
  const colorStyle = (index: number) =>
    colors.length === 0 ? undefined : { backgroundColor: colors[index % colors.length] };

  return (
    <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex flex-wrap items-baseline gap-3">
          <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">{title}</p>
          <p className="text-sm font-semibold uppercase tracking-[0.2em] text-slate-700 dark:text-slate-200">{subtitle}</p>
        </div>
        {onOpenRecap && (
          <button
            type="button"
            onClick={onOpenRecap}
            className="flex h-9 w-9 items-center justify-center rounded-xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/60 text-slate-600 dark:text-slate-200 transition hover:border-emerald-300 hover:text-emerald-700 dark:hover:text-emerald-200"
            aria-label="Open recap"
            title="Open recap"
          >
            <svg
              xmlns="http://www.w3.org/2000/svg"
              width="24"
              height="24"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
              className="lucide lucide-chart-column h-4 w-4"
              aria-hidden="true"
            >
              <path d="M3 3v16a2 2 0 0 0 2 2h16"></path>
              <path d="M18 17V9"></path>
              <path d="M13 17V5"></path>
              <path d="M8 17v-3"></path>
            </svg>
          </button>
        )}
      </div>
      <div className="mt-5 grid gap-4 sm:grid-cols-3">
        {summary.map((stat) => (
          <div key={stat.label} className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4">
            <p className="text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">{stat.label}</p>
            <p className="mt-2 text-lg font-semibold text-slate-900 dark:text-white">{stat.value}</p>
          </div>
        ))}
      </div>
      <div className="mt-5 grid gap-4 lg:grid-cols-[0.9fr_1.1fr]">
        <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4">
          <p className="text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">Platform distribution</p>
          <div className="mt-4 flex flex-wrap items-center gap-4">
            <StatsPieChart
              segments={pieSegments}
              colors={colors}
              size={168}
              selectedKey={selectedPlatform}
              onSegmentSelect={onPlatformSelect}
            />
            <div className="flex-1 space-y-2 text-xs text-slate-600 dark:text-slate-300">
              {platforms.map((item, index) =>
                onPlatformSelect ? (
                  <button
                    key={index}
                    type="button"
                    onClick={() => onPlatformSelect(item.key)}
                    aria-pressed={selectedPlatform === item.key ? "true" : "false"}
                    className={`flex w-full items-center justify-between gap-3 rounded-lg px-2 py-1 text-left transition hover:bg-slate-200/70 dark:hover:bg-slate-800/70 ${
                      selectedPlatform === item.key
                        ? "bg-emerald-100/80 text-emerald-900 ring-1 ring-emerald-400/60 dark:bg-emerald-400/10 dark:text-emerald-100"
                        : selectedPlatform.length > 0
                          ? "opacity-55"
                          : ""
                    }`}
                  >
                    <span className="flex items-center gap-2">
                      <span className="h-2 w-2 rounded-full" style={colorStyle(index)}></span>
                      <span>{item.label}</span>
                    </span>
                    <span>
                      {item.value}% · {item.hours}
                    </span>
                  </button>
                ) : (
                  <div key={index} className="flex items-center justify-between gap-3">
                    <div className="flex items-center gap-2">
                      <span className="h-2 w-2 rounded-full" style={colorStyle(index)}></span>
                      <span>{item.label}</span>
                    </div>
                    <span>
                      {item.value}% · {item.hours}
                    </span>
                  </div>
                ),
              )}
            </div>
          </div>
        </div>
        <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4">
          <p className="text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">Top 5 games</p>
          <div className="mt-4 space-y-3">
            {topGames.map((game, index) => (
              <div key={`${game.gameId}-${game.title}`} className="flex items-center justify-between text-sm">
                <div className="flex items-center gap-3">
                  <span className="text-xs font-semibold text-slate-500 dark:text-slate-400">{String(index + 1).padStart(2, "0")}</span>
                  {onGameSelect ? (
                    <button
                      type="button"
                      onClick={() => onGameSelect(game.gameId)}
                      className="text-left font-semibold text-slate-900 dark:text-white transition hover:text-emerald-700 dark:hover:text-emerald-200"
                    >
                      {game.title}
                    </button>
                  ) : (
                    <span className="font-semibold text-slate-900 dark:text-white">{game.title}</span>
                  )}
                </div>
                <span className="text-xs font-semibold text-emerald-700 dark:text-emerald-200">{game.hours}</span>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}
