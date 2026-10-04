import { Cover } from "@/components/ui";
import { cx } from "@/lib/cx";
import type { GameConnection, GameSearchResult } from "@/models/games";
import { SectionHeading } from "../shared/SectionHeading";
import type { GameSearchState, SearchSlot } from "./mappingState";
import { useMapping } from "./useMapping";

type Mapping = ReturnType<typeof useMapping>;

const platformLabel = (platform: string | null | undefined) => (platform ? platform : "Unknown platform");

const secondaryCoverStyle = { background: "linear-gradient(135deg,#8a96a3,#b5bfc9)" };

export function MappingSection() {
  const mapping = useMapping();
  const { state } = mapping;
  const primary = state.primary.selected;
  const secondary = state.secondary.selected;
  const canConnect =
    primary !== null && secondary !== null && primary.gameId !== secondary.gameId && !state.isConnectionSaving;
  const pairCount = state.gameConnections.length;

  return (
    <section id="game-connections" data-settings-section="game-connections" className="pdx-card flex flex-col gap-6">
      <SectionHeading
        kicker="Game connections"
        title="Merge stats across editions"
        description="Combine two library entries so their playtime counts as one game."
        aside={
          <button
            type="button"
            onClick={() => void mapping.loadGameConnections()}
            disabled={state.isGameConnectionsLoading}
            className="pdx-btn-outline !h-[38px] !px-[18px] !text-[13px]"
          >
            <span aria-hidden="true">↻</span>
            {state.isGameConnectionsLoading ? "Refreshing..." : "Refresh"}
          </button>
        }
      />

      {state.gameConnectionsError.length > 0 ? (
        <p role="alert" className="text-[13px] text-danger">
          {state.gameConnectionsError}
        </p>
      ) : null}

      <div className="pdx-tile flex flex-col gap-3 !p-6">
        <div className="grid items-start gap-3 md:grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)_auto]">
          <GameSearchField
            slot="primary"
            title="Primary game"
            emptyHint="Search to find the primary game."
            search={state.primary}
            mapping={mapping}
          />
          <div className="hidden flex-col gap-2 md:flex" aria-hidden="true">
            <span className="pdx-label">&nbsp;</span>
            <span className="grid h-12 place-items-center text-lg text-accent">⟵</span>
          </div>
          <GameSearchField
            slot="secondary"
            title="Merge into it"
            emptyHint="Search to find the game to merge in."
            search={state.secondary}
            mapping={mapping}
          />
          <div className="flex flex-col gap-2">
            <span className="pdx-label hidden md:block" aria-hidden="true">
              &nbsp;
            </span>
            <div className="flex gap-2">
              <button type="button" onClick={mapping.clearConnectionDraft} className="pdx-btn-outline !h-12 !px-[18px]">
                Clear
              </button>
              <button
                type="button"
                onClick={() => void mapping.connectGames()}
                disabled={!canConnect}
                className="pdx-btn-primary !h-12 !px-[22px]"
              >
                {state.isConnectionSaving ? "Saving..." : "Connect"}
              </button>
            </div>
          </div>
        </div>
        {state.connectionSaveError.length > 0 ? (
          <p role="alert" className="text-[13px] text-danger">
            {state.connectionSaveError}
          </p>
        ) : null}
      </div>

      <div className="flex flex-col gap-1">
        <div className="pdx-label flex justify-between px-1 pb-2">
          <span>Connected pairs</span>
          {!state.isGameConnectionsLoading ? (
            <span className="pdx-num">
              {pairCount} {pairCount === 1 ? "pair" : "pairs"}
            </span>
          ) : null}
        </div>
        {state.isGameConnectionsLoading ? (
          [1, 2, 3].map((row) => (
            <div key={row} aria-hidden="true" className="flex items-center gap-3 px-3.5 py-3">
              <div className="h-11 w-11 animate-pulse rounded-xl bg-track"></div>
              <div className="h-3 w-40 max-w-full animate-pulse rounded-full bg-track"></div>
            </div>
          ))
        ) : pairCount === 0 ? (
          <div className="pdx-dashed !rounded-[20px] border-[1.5px] !p-5 !text-sm">
            No games connected yet. Pick two above to merge their playtime.
          </div>
        ) : (
          state.gameConnections.map((connection) => (
            <ConnectionRow key={`${connection.gameId}-${connection.connectedGameId}`} connection={connection} mapping={mapping} />
          ))
        )}
      </div>
    </section>
  );
}

interface GameSearchFieldProps {
  slot: SearchSlot;
  title: string;
  emptyHint: string;
  search: GameSearchState;
  mapping: Mapping;
}

function GameSearchField({ slot, title, emptyHint, search, mapping }: GameSearchFieldProps) {
  const selected = search.selected;
  const inputId = `game-connection-${slot}`;
  const showSelected = selected !== null && search.query === selected.title;

  return (
    <div className="flex min-w-0 flex-col gap-2">
      <label htmlFor={inputId} className="pdx-label">
        {title}
      </label>
      <div className="relative">
        <input
          id={inputId}
          type="text"
          value={search.query}
          onChange={(event) => mapping.changeGameQuery(slot, event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter") {
              event.preventDefault();
              void mapping.searchGames(slot);
            }
          }}
          placeholder="Search your library"
          className={cx("pdx-input !h-12 !pl-5 !pr-[104px]", showSelected && "!border-mint-line")}
        />
        <button
          type="button"
          onClick={() => void mapping.searchGames(slot)}
          disabled={search.isLoading}
          className="pdx-btn-accent absolute right-1.5 top-1.5 !h-9 !px-3.5 !text-xs"
        >
          {search.isLoading ? "Searching..." : "Search"}
        </button>
      </div>
      {search.error.length > 0 ? <p className="px-1 text-xs text-danger">{search.error}</p> : null}
      {search.results.length > 0 ? (
        <ul className="flex max-h-72 flex-col gap-0.5 overflow-y-auto rounded-[18px] border border-line bg-surface p-1.5 shadow-pop">
          {search.results.map((result) => (
            <li key={result.gameId}>
              <SearchResultButton result={result} onSelect={() => mapping.selectGame(slot, result)} />
            </li>
          ))}
        </ul>
      ) : showSelected ? (
        <p className="flex items-center gap-1.5 px-1 text-xs font-semibold text-accent">
          <span aria-hidden="true">✓</span>
          <span className="truncate">Selected · {platformLabel(selected.platform)}</span>
        </p>
      ) : (
        <p className="px-1 text-xs text-muted">{emptyHint}</p>
      )}
    </div>
  );
}

function SearchResultButton({ result, onSelect }: { result: GameSearchResult; onSelect: () => void }) {
  return (
    <button
      type="button"
      onClick={onSelect}
      className="pdx-row flex w-full items-center gap-3 !rounded-[14px] px-2.5 py-2 text-left"
    >
      <Cover title={result.title} src={result.capsuleUrl} className="h-9 w-9 rounded-[10px]" initialClassName="text-sm" />
      <span className="min-w-0">
        <span className="block truncate text-sm font-semibold">{result.title}</span>
        <span className="block text-xs text-muted">{platformLabel(result.platform)}</span>
      </span>
    </button>
  );
}

function ConnectionRow({ connection, mapping }: { connection: GameConnection; mapping: Mapping }) {
  return (
    <div className="pdx-row grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3.5 gap-y-2 !rounded-[20px] px-3.5 py-3 sm:grid-cols-[minmax(0,1fr)_32px_minmax(0,1fr)_auto]">
      <ConnectionGame
        title={connection.title}
        capsuleUrl={connection.capsuleUrl}
        role="Primary"
        platform={connection.platform}
        onOpen={() => mapping.navigateToGame(connection.gameId)}
        primary
      />
      <span
        aria-hidden="true"
        className="hidden h-8 w-8 place-items-center rounded-full bg-mint text-sm text-accent sm:grid"
      >
        ⟵
      </span>
      <div className="col-start-1 row-start-2 min-w-0 sm:col-start-auto sm:row-start-auto">
        <ConnectionGame
          title={connection.connectedTitle}
          capsuleUrl={connection.connectedCapsuleUrl}
          role="Merged in"
          platform={connection.connectedPlatform}
          onOpen={() => mapping.navigateToGame(connection.connectedGameId)}
        />
      </div>
      <button
        type="button"
        onClick={() => void mapping.disconnectGames(connection.gameId, connection.connectedGameId)}
        disabled={mapping.state.isConnectionSaving}
        aria-label={`Unlink ${connection.connectedTitle} from ${connection.title}`}
        className="pdx-btn-danger col-start-2 row-span-2 row-start-1 !h-8 !px-3.5 !text-xs sm:col-start-auto sm:row-span-1 sm:row-start-auto"
      >
        Unlink
      </button>
    </div>
  );
}

function ConnectionGame({
  title,
  capsuleUrl,
  role,
  platform,
  onOpen,
  primary = false,
}: {
  title: string;
  capsuleUrl: string | null;
  role: string;
  platform: string;
  onOpen: () => void;
  primary?: boolean;
}) {
  return (
    <div className="flex min-w-0 items-center gap-3">
      <Cover
        title={title}
        src={capsuleUrl}
        className="h-11 w-11 rounded-xl"
        initialClassName="text-[15px]"
        style={primary ? undefined : secondaryCoverStyle}
      />
      <div className="min-w-0">
        <button
          type="button"
          onClick={onOpen}
          className="block max-w-full truncate text-left text-[15px] font-medium transition-colors hover:text-accent"
        >
          {title}
        </button>
        <p className={cx("truncate text-xs", primary ? "font-semibold text-accent" : "text-muted")}>
          {role}
          <span className="font-normal text-muted"> · {platformLabel(platform)}</span>
        </p>
      </div>
    </div>
  );
}
