import { StatsPieChart } from "./StatsPieChart";

type SummaryStat = {
  label: string;
  value: string;
};

type PlatformStat = {
  label: string;
  value: number;
  hours: string;
};

type TopGameStat = {
  gameId: string;
  title: string;
  hours: string;
};

type StatsRecapSectionProps = {
  title: string;
  subtitle: string;
  summary: SummaryStat[];
  platforms: PlatformStat[];
  topGames: TopGameStat[];
  colors: string[];
  onGameSelect?: (gameId: string) => void;
  onOpenRecap?: () => void;
};

export const StatsRecapSection = ({
  title,
  subtitle,
  summary,
  platforms,
  topGames,
  colors,
  onGameSelect,
  onOpenRecap,
}: StatsRecapSectionProps) => {
  return (
    <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex flex-wrap items-baseline gap-3">
          <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
            {title}
          </p>
          <p className="text-sm font-semibold uppercase tracking-[0.2em] text-slate-700 dark:text-slate-200">
            {subtitle}
          </p>
        </div>
        {onOpenRecap ? (
          <button
            type="button"
            onClick={onOpenRecap}
            className="flex h-9 w-9 items-center justify-center rounded-xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/60 text-slate-600 dark:text-slate-200 transition hover:border-emerald-300 hover:text-emerald-700 dark:hover:text-emerald-200"
            aria-label="Open recap"
            title="Open recap"
          >
            <svg
              viewBox="0 0 20 20"
              className="h-4 w-4"
              fill="currentColor"
              aria-hidden="true"
            >
              <path d="M12.5 3.5a1 1 0 011.5.86v7.79l2.22-2.22a1 1 0 111.41 1.41l-3.93 3.93a1 1 0 01-1.7-.7V4.36a1 1 0 01.5-.86z" />
              <path d="M4 5a2 2 0 012-2h3a1 1 0 110 2H6v10h8v-3a1 1 0 112 0v3a2 2 0 01-2 2H6a2 2 0 01-2-2V5z" />
            </svg>
          </button>
        ) : null}
      </div>
      <div className="mt-5 grid gap-4 sm:grid-cols-3">
        {summary.map((stat) => (
          <div
            key={stat.label}
            className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4"
          >
            <p className="text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
              {stat.label}
            </p>
            <p className="mt-2 text-lg font-semibold text-slate-900 dark:text-white">
              {stat.value}
            </p>
          </div>
        ))}
      </div>
      <div className="mt-5 grid gap-4 lg:grid-cols-[0.9fr_1.1fr]">
        <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4">
          <p className="text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
            Platform distribution
          </p>
          <div className="mt-4 flex flex-wrap items-center gap-4">
            <StatsPieChart segments={platforms} colors={colors} size={168} />
            <div className="flex-1 space-y-2 text-xs text-slate-600 dark:text-slate-300">
              {platforms.map((item, index) => (
                <div
                  key={item.label}
                  className="flex items-center justify-between gap-3"
                >
                  <div className="flex items-center gap-2">
                    <span
                      className="h-2 w-2 rounded-full"
                      style={{
                        backgroundColor: colors[index % colors.length],
                      }}
                    />
                    <span>{item.label}</span>
                  </div>
                  <span>
                    {item.value}% · {item.hours}
                  </span>
                </div>
              ))}
            </div>
          </div>
        </div>
        <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4">
          <p className="text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
            Top 5 games
          </p>
          <div className="mt-4 space-y-3">
            {topGames.map((game, index) => (
              <div
                key={`${game.gameId}-${game.title}`}
                className="flex items-center justify-between text-sm"
              >
                <div className="flex items-center gap-3">
                  <span className="text-xs font-semibold text-slate-500 dark:text-slate-400">
                    {String(index + 1).padStart(2, "0")}
                  </span>
                  {onGameSelect ? (
                    <button
                      type="button"
                      onClick={() => onGameSelect(game.gameId)}
                      className="text-left font-semibold text-slate-900 dark:text-white transition hover:text-emerald-700 dark:hover:text-emerald-200"
                    >
                      {game.title}
                    </button>
                  ) : (
                    <span className="font-semibold text-slate-900 dark:text-white">
                      {game.title}
                    </span>
                  )}
                </div>
                <span className="text-xs font-semibold text-emerald-700 dark:text-emerald-200">
                  {game.hours}
                </span>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
};
