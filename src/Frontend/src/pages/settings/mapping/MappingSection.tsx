import type { GameConnection, GameSearchResult } from "@/models/games";
import type { GameSearchState, SearchSlot } from "./mappingState";
import { useMapping } from "./useMapping";

type Mapping = ReturnType<typeof useMapping>;

const platformLabel = (platform: string | null | undefined) => (platform ? platform : "Unknown platform");

export function MappingSection() {
  const mapping = useMapping();
  const { state } = mapping;

  return (
    <section
      id="game-connections"
      data-settings-section="game-connections"
      className="rounded-3xl border border-slate-200 bg-white/80 p-5 dark:border-slate-800 dark:bg-slate-900/60 sm:p-6"
    >
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
            Game connections
          </p>
          <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">Merge stats across editions</h2>
        </div>
        <button
          type="button"
          onClick={() => void mapping.loadGameConnections()}
          disabled={state.isGameConnectionsLoading}
          className="rounded-full border border-slate-300 dark:border-slate-700 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-700 dark:text-slate-200 transition hover:border-emerald-300/60 dark:hover:border-emerald-400/60 disabled:cursor-not-allowed"
        >
          {state.isGameConnectionsLoading ? "Refreshing..." : "Refresh"}
        </button>
      </div>

      {state.gameConnectionsError.length > 0 ? (
        <p className="mt-4 text-xs text-rose-700 dark:text-rose-300">{state.gameConnectionsError}</p>
      ) : null}

      <div className="mt-6 grid gap-4 lg:grid-cols-[1.1fr_0.9fr]">
        <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/50 p-4 sm:p-5">
          <p className="text-xs font-semibold uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
            Connect games
          </p>
          <p className="mt-2 text-sm text-slate-600 dark:text-slate-300">
            Pick two games to combine their playtime into a single entry.
          </p>

          <div className="mt-4 grid gap-4">
            <GameSearchPanel
              slot="primary"
              title="Primary game"
              emptyHint="Search to find the primary game."
              search={state.primary}
              mapping={mapping}
            />
            <GameSearchPanel
              slot="secondary"
              title="Connected game"
              emptyHint="Search to find the connected game."
              search={state.secondary}
              mapping={mapping}
            />
          </div>

          {state.connectionSaveError.length > 0 ? (
            <p className="mt-4 text-xs text-rose-700 dark:text-rose-300">{state.connectionSaveError}</p>
          ) : null}

          <div className="mt-4 flex flex-wrap gap-3">
            <button
              type="button"
              onClick={() => void mapping.connectGames()}
              disabled={state.isConnectionSaving}
              className="rounded-full bg-emerald-300 dark:bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-200 dark:hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-emerald-200/60 dark:disabled:bg-emerald-300/60"
            >
              {state.isConnectionSaving ? "Saving..." : "Connect games"}
            </button>
            <button
              type="button"
              onClick={mapping.clearConnectionDraft}
              className="rounded-full border border-slate-300 dark:border-slate-700 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-700 dark:text-slate-200 transition hover:border-emerald-300/60 dark:hover:border-emerald-400/60"
            >
              Clear
            </button>
          </div>
        </div>

        <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/50 p-4 sm:p-5">
          <p className="text-xs font-semibold uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
            Connected pairs
          </p>
          <div className="mt-4 space-y-3">
            {state.isGameConnectionsLoading ? (
              [1, 2, 3].map((row) => (
                <div
                  key={row}
                  className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/40 p-3"
                >
                  <div className="flex items-center gap-3">
                    <div className="h-10 w-8 rounded-lg bg-slate-200/70 dark:bg-slate-800/60"></div>
                    <div className="h-3 w-24 rounded-full bg-slate-200/70 dark:bg-slate-800/60"></div>
                  </div>
                </div>
              ))
            ) : state.gameConnections.length === 0 ? (
              <div className="rounded-2xl border border-dashed border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/40 p-4 text-xs text-slate-500 dark:text-slate-400">
                No game connections yet.
              </div>
            ) : (
              state.gameConnections.map((connection) => (
                <ConnectionRow
                  key={`${connection.gameId}-${connection.connectedGameId}`}
                  connection={connection}
                  mapping={mapping}
                />
              ))
            )}
          </div>
        </div>
      </div>
    </section>
  );
}

interface GameSearchPanelProps {
  slot: SearchSlot;
  title: string;
  emptyHint: string;
  search: GameSearchState;
  mapping: Mapping;
}

function GameSearchPanel({ slot, title, emptyHint, search, mapping }: GameSearchPanelProps) {
  const selected = search.selected;

  return (
    <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/60 p-4">
      <p className="text-xs font-semibold uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">{title}</p>
      <div className="mt-3 flex flex-wrap items-center gap-3">
        <input
          type="text"
          value={search.query}
          onChange={(event) => mapping.changeGameQuery(slot, event.target.value)}
          className="flex-1 rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-300 dark:focus:border-emerald-400 focus:ring-2 focus:ring-emerald-300/30 dark:focus:ring-emerald-400/30"
          placeholder="Search games"
        />
        <button
          type="button"
          onClick={() => void mapping.searchGames(slot)}
          disabled={search.isLoading}
          className="rounded-full bg-emerald-300 dark:bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-200 dark:hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-emerald-200/60 dark:disabled:bg-emerald-300/60"
        >
          {search.isLoading ? "Searching..." : "Search"}
        </button>
      </div>
      {search.error.length > 0 ? <p className="mt-3 text-xs text-rose-700 dark:text-rose-300">{search.error}</p> : null}
      {selected !== null ? (
        <div className="mt-3 flex items-center gap-3 rounded-xl border border-emerald-300/40 dark:border-emerald-400/40 bg-emerald-300/10 dark:bg-emerald-500/10 p-3 text-sm text-emerald-700 dark:text-emerald-100">
          {selected.capsuleUrl ? (
            <img
              src={selected.capsuleUrl}
              alt={`${selected.title} cover`}
              className="h-10 w-8 rounded-lg border border-emerald-300/40 dark:border-emerald-400/40 object-cover"
            />
          ) : (
            <div className="h-10 w-8 rounded-lg border border-emerald-300/40 dark:border-emerald-400/40 bg-emerald-300/10 dark:bg-emerald-500/10"></div>
          )}
          <div>
            <p className="font-semibold">{selected.title}</p>
            <p className="text-xs text-emerald-700 dark:text-emerald-200">{platformLabel(selected.platform)}</p>
          </div>
        </div>
      ) : null}
      <div className="mt-3 grid gap-2">
        {search.results.length === 0 ? (
          <p className="text-xs text-slate-500">{emptyHint}</p>
        ) : (
          search.results.map((result) => (
            <SearchResultButton key={result.gameId} result={result} onSelect={() => mapping.selectGame(slot, result)} />
          ))
        )}
      </div>
    </div>
  );
}

function SearchResultButton({ result, onSelect }: { result: GameSearchResult; onSelect: () => void }) {
  return (
    <button
      type="button"
      onClick={onSelect}
      className="flex items-center gap-3 rounded-xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-950/40 p-3 text-left text-sm text-slate-900 dark:text-white transition hover:border-emerald-300/60 dark:hover:border-emerald-400/60"
    >
      <Capsule url={result.capsuleUrl} title={result.title} />
      <div>
        <p className="font-semibold">{result.title}</p>
        <p className="text-xs text-slate-500 dark:text-slate-400">{platformLabel(result.platform)}</p>
      </div>
    </button>
  );
}

function Capsule({ url, title }: { url: string | null; title: string }) {
  return url ? (
    <img
      src={url}
      alt={`${title} cover`}
      className="h-10 w-8 rounded-lg border border-slate-200 dark:border-slate-800 object-cover"
    />
  ) : (
    <div className="h-10 w-8 rounded-lg border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60"></div>
  );
}

function ConnectionRow({ connection, mapping }: { connection: GameConnection; mapping: Mapping }) {
  return (
    <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-950/40 p-3">
      <div className="flex flex-col gap-3">
        <div className="flex items-center gap-3">
          <Capsule url={connection.capsuleUrl} title={connection.title} />
          <div className="min-w-0 flex-1">
            <button
              type="button"
              onClick={() => mapping.navigateToGame(connection.gameId)}
              className="block w-full break-words text-left text-sm font-semibold text-slate-900 dark:text-white transition hover:text-emerald-700 dark:hover:text-emerald-200"
            >
              {connection.title}
            </button>
            <p className="text-xs text-slate-500 dark:text-slate-400">{platformLabel(connection.platform)}</p>
          </div>
        </div>
        <div className="flex items-center gap-2 text-xs uppercase tracking-[0.3em] text-slate-500">
          <span className="h-px flex-1 bg-slate-200 dark:bg-slate-800"></span>
          <span>+ +</span>
          <span className="h-px flex-1 bg-slate-200 dark:bg-slate-800"></span>
        </div>
        <div className="flex items-center gap-3">
          <Capsule url={connection.connectedCapsuleUrl} title={connection.connectedTitle} />
          <div className="min-w-0 flex-1">
            <button
              type="button"
              onClick={() => mapping.navigateToGame(connection.connectedGameId)}
              className="block w-full break-words text-left text-sm font-semibold text-slate-900 dark:text-white transition hover:text-emerald-700 dark:hover:text-emerald-200"
            >
              {connection.connectedTitle}
            </button>
            <p className="text-xs text-slate-500 dark:text-slate-400">{platformLabel(connection.connectedPlatform)}</p>
          </div>
        </div>
      </div>
      <div className="mt-3 flex justify-end">
        <button
          type="button"
          onClick={() => void mapping.disconnectGames(connection.gameId, connection.connectedGameId)}
          disabled={mapping.state.isConnectionSaving}
          className="rounded-full border border-rose-400/60 px-3 py-1 text-[10px] font-semibold uppercase tracking-[0.3em] text-rose-700 dark:text-rose-200 transition hover:border-rose-300 hover:text-rose-700 dark:hover:text-rose-100 disabled:cursor-not-allowed"
        >
          Remove
        </button>
      </div>
    </div>
  );
}
