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
  onGameSelect?: (title: string) => void;
};

export const StatsRecapSection = ({
  title,
  subtitle,
  summary,
  platforms,
  topGames,
  colors,
  onGameSelect,
}: StatsRecapSectionProps) => {
  return (
    <div className="rounded-3xl border border-slate-800 bg-slate-900/60 p-5 sm:p-6">
      <div className="flex flex-wrap items-baseline gap-3">
        <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-300">
          {title}
        </p>
        <p className="text-sm font-semibold uppercase tracking-[0.2em] text-slate-200">
          {subtitle}
        </p>
      </div>
      <div className="mt-5 grid gap-4 sm:grid-cols-3">
        {summary.map((stat) => (
          <div
            key={stat.label}
            className="rounded-2xl border border-slate-800 bg-slate-950/50 p-4"
          >
            <p className="text-xs uppercase tracking-[0.3em] text-slate-400">
              {stat.label}
            </p>
            <p className="mt-2 text-lg font-semibold text-white">
              {stat.value}
            </p>
          </div>
        ))}
      </div>
      <div className="mt-5 grid gap-4 lg:grid-cols-[0.9fr_1.1fr]">
        <div className="rounded-2xl border border-slate-800 bg-slate-950/50 p-4">
          <p className="text-xs uppercase tracking-[0.3em] text-slate-400">
            Platform distribution
          </p>
          <div className="mt-4 flex flex-wrap items-center gap-4">
            <StatsPieChart segments={platforms} colors={colors} size={168} />
            <div className="flex-1 space-y-2 text-xs text-slate-300">
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
        <div className="rounded-2xl border border-slate-800 bg-slate-950/50 p-4">
          <p className="text-xs uppercase tracking-[0.3em] text-slate-400">
            Top 5 games
          </p>
          <div className="mt-4 space-y-3">
            {topGames.map((game, index) => (
              <div
                key={game.title}
                className="flex items-center justify-between text-sm"
              >
                <div className="flex items-center gap-3">
                  <span className="text-xs font-semibold text-slate-400">
                    {String(index + 1).padStart(2, "0")}
                  </span>
                  {onGameSelect ? (
                    <button
                      type="button"
                      onClick={() => onGameSelect(game.title)}
                      className="text-left font-semibold text-white transition hover:text-emerald-200"
                    >
                      {game.title}
                    </button>
                  ) : (
                    <span className="font-semibold text-white">
                      {game.title}
                    </span>
                  )}
                </div>
                <span className="text-xs font-semibold text-emerald-200">
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
