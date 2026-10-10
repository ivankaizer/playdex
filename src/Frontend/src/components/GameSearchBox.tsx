import { useEffect, useRef, useState, type ChangeEvent, type KeyboardEvent } from "react";
import { errorMessage, isAbortError } from "@/lib/api";
import type { GameSearchResponse, GameSearchResult } from "@/models/games";

interface GameSearchBoxProps {
  search: (path: string, signal: AbortSignal) => Promise<GameSearchResponse>;
  onNavigate: (path: string) => void;
}

interface SearchState {
  query: string;
  results: GameSearchResult[];
  isLoading: boolean;
  isOpen: boolean;
  activeIndex: number;
  error: string;
}

const emptyState: SearchState = {
  query: "",
  results: [],
  isLoading: false,
  isOpen: false,
  activeIndex: -1,
  error: "",
};

const optionId = (index: number) => `game-search-option-${index}`;

export function GameSearchBox({ search, onNavigate }: GameSearchBoxProps) {
  const [state, setState] = useState<SearchState>(emptyState);
  const requestRef = useRef<AbortController | null>(null);

  useEffect(() => () => requestRef.current?.abort(), []);

  const invalidateSearch = () => {
    requestRef.current?.abort();
    requestRef.current = null;
  };

  const handleInput = async (event: ChangeEvent<HTMLInputElement>) => {
    invalidateSearch();
    const query = event.target.value;
    setState({ ...emptyState, query });
    const trimmed = query.trim();
    if (trimmed.length === 0) return;

    const request = new AbortController();
    requestRef.current = request;
    try {
      await delay(250, request.signal);
      setState((current) => ({ ...current, isLoading: true, isOpen: true, error: "" }));
      const response = await search(`/games/search?query=${encodeURIComponent(trimmed)}&limit=5`, request.signal);
      if (requestRef.current !== request || request.signal.aborted) return;
      setState((current) => ({
        ...current,
        results: response.results.slice(0, 5),
        activeIndex: -1,
        isLoading: false,
        isOpen: true,
        error: "",
      }));
    } catch (error) {
      if (request.signal.aborted || isAbortError(error)) return;
      if (requestRef.current !== request) return;
      setState((current) => ({
        ...current,
        results: [],
        activeIndex: -1,
        isLoading: false,
        isOpen: true,
        error: errorMessage(error),
      }));
    } finally {
      if (requestRef.current === request) requestRef.current = null;
    }
  };

  const moveActive = (direction: number) =>
    setState((current) =>
      current.results.length === 0
        ? current
        : {
            ...current,
            activeIndex: Math.min(Math.max(current.activeIndex + direction, 0), current.results.length - 1),
          },
    );

  const navigateTo = (path: string) => {
    invalidateSearch();
    onNavigate(path);
  };

  const dismiss = () => {
    invalidateSearch();
    setState((current) => ({ ...current, isOpen: false, activeIndex: -1 }));
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    switch (event.key) {
      case "ArrowDown":
        moveActive(1);
        break;
      case "ArrowUp":
        moveActive(-1);
        break;
      case "Enter": {
        const selected =
          state.activeIndex >= 0 && state.activeIndex < state.results.length ? state.results[state.activeIndex] : null;
        if (selected) {
          setState(emptyState);
          navigateTo(`/games/${selected.gameId}`);
        }
        break;
      }
      case "Escape":
        dismiss();
        break;
    }
  };

  const select = (result: GameSearchResult) => {
    setState(emptyState);
    navigateTo(`/games/${result.gameId}`);
  };

  const activeOptionId = state.activeIndex < 0 ? undefined : optionId(state.activeIndex);

  return (
    // At ≤600px the box is static so the results anchor to the header card and span its width instead of
    // overflowing the left edge of a narrow screen.
    <div className="relative z-50 w-full max-[600px]:static">
      <label htmlFor="global-game-search" className="sr-only">
        Search games
      </label>
      <div className="relative">
        <svg
          aria-hidden="true"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="1.75"
          className="pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-faint"
        >
          <circle cx="11" cy="11" r="7"></circle>
          <path d="m20 20-3.5-3.5"></path>
        </svg>
        <input
          id="global-game-search"
          type="search"
          role="combobox"
          aria-label="Search games"
          aria-autocomplete="list"
          aria-controls="game-search-results"
          aria-expanded={state.isOpen ? "true" : "false"}
          aria-activedescendant={activeOptionId}
          autoComplete="off"
          placeholder="Search games"
          value={state.query}
          onChange={handleInput}
          onKeyDown={handleKeyDown}
          className="pdx-input !h-[38px] !pl-10 !pr-4 !text-[13px]"
        />
      </div>

      {state.isOpen && (
        <>
          <button
            type="button"
            aria-label="Close game search"
            onClick={dismiss}
            className="fixed inset-0 z-[-1] cursor-default bg-transparent"
          ></button>
          <div
            id="game-search-results"
            role="listbox"
            aria-label="Game search results"
            className="absolute right-0 top-full z-50 mt-2 w-72 overflow-hidden rounded-[22px] border border-line bg-surface p-1.5 shadow-pop sm:w-80 max-[600px]:left-0 max-[600px]:w-auto"
          >
            {state.isLoading ? (
              <p className="px-4 py-3 text-sm text-muted">Searching...</p>
            ) : state.error.length > 0 ? (
              <p className="px-4 py-3 text-sm text-danger">{state.error}</p>
            ) : state.results.length === 0 ? (
              <p className="px-4 py-3 text-sm text-muted">No games found.</p>
            ) : (
              state.results.map((result, index) => (
                <button
                  key={result.gameId}
                  id={optionId(index)}
                  type="button"
                  role="option"
                  aria-selected={index === state.activeIndex}
                  onMouseEnter={() => moveActive(index - state.activeIndex)}
                  onClick={() => select(result)}
                  className={`flex w-full items-center gap-3 rounded-2xl px-3 py-2 text-left transition hover:bg-tile ${index === state.activeIndex ? "bg-mint" : ""}`}
                >
                  {result.capsuleUrl !== null ? (
                    <img
                      src={result.capsuleUrl}
                      alt=""
                      className="h-12 w-9 shrink-0 rounded-[10px] object-cover"
                    />
                  ) : (
                    <span aria-hidden="true" className="h-12 w-9 shrink-0 rounded-[10px] bg-track"></span>
                  )}
                  <span className="min-w-0">
                    <span className="block truncate text-sm font-semibold text-ink">{result.title}</span>
                    <span className="block truncate text-xs text-muted">{result.platform}</span>
                  </span>
                </button>
              ))
            )}
          </div>
        </>
      )}
    </div>
  );
}

function delay(milliseconds: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const timer = window.setTimeout(resolve, milliseconds);
    signal.addEventListener(
      "abort",
      () => {
        window.clearTimeout(timer);
        reject(new DOMException("Aborted", "AbortError"));
      },
      { once: true },
    );
  });
}
