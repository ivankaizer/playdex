import { Check, ImageOff, RotateCcw, Search } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { closeArtPicker, showToast } from "../app-state";
import { gameById, setImages } from "../data/games";
import { inHost } from "../host/bridge";
import {
  desktopState,
  rpc,
  type ArtImage,
  type ArtKind,
  type ArtPage,
  type ArtSearchView,
  type ArtSource,
  type ArtView,
  type PickedArt,
} from "../host/desktop";
import { focusDefault, focusEl } from "../nav/focus";
import { onAction } from "../nav/input";
import { useStore } from "../nav/store";
import { Hint } from "./shared";
import "./art-picker.css";

const KINDS: { kind: ArtKind; label: string }[] = [
  { kind: "portrait", label: "Poster" },
  { kind: "capsule", label: "Wide cover" },
  { kind: "hero", label: "Background" },
  { kind: "logo", label: "Logo" },
];

const labelOf = (kind: ArtKind) => KINDS.find((k) => k.kind === kind)!.label;

const message = (e: unknown) => (e instanceof Error ? e.message : String(e));

/** Focus the tab of an artwork kind once it has rendered. */
const focusKind = (kind: ArtKind) =>
  window.setTimeout(() => focusEl(document.querySelector<HTMLElement>(`[data-art-kind="${kind}"]`)));

/**
 * Change a game's poster, wide cover, background and logo to images from SteamGridDB. The game is matched on
 * SteamGridDB by its Steam app or its title; "Not this game?" searches for another. A picked image replaces the
 * game's art everywhere at once; Use default goes back to the art it had. A panel over the game's page (inert while it
 * is open): LB and RB switch the artwork kind, B closes it.
 */
export function ArtPicker({ gameId }: { gameId: string }) {
  const game = gameById(gameId);
  const keySet = useStore(desktopState)?.settings.steamGridDbKeySet;
  // Only the app knows whether a key is saved; the browser stand-in has no SteamGridDB to ask.
  const noKey = inHost && keySet === false;
  const [kind, setKind] = useState<ArtKind>("portrait");
  const [sources, setSources] = useState<ArtSource[] | null>(null);
  const [source, setSource] = useState<ArtSource | null>(null);
  const [choosing, setChoosing] = useState(false);
  const [query, setQuery] = useState(game.title);
  const [picked, setPicked] = useState<PickedArt>({});
  const [page, setPage] = useState<(ArtPage & { next: number }) | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<number | "reset" | "more" | "search" | null>(null);
  const loadToken = useRef(0);
  const state = useRef({ kind, choosing });
  state.current = { kind, choosing };

  useEffect(() => {
    const t = window.setTimeout(focusDefault);
    if (noKey) return () => window.clearTimeout(t);
    rpc<ArtSearchView>("art.search", { libraryId: gameId })
      .then((r) => {
        setSources(r.games);
        setPicked(r.picked);
        setSource(r.games[0] ?? null);
        if (!r.games.length) openChoosing();
      })
      .catch((e: unknown) => setError(message(e)));
    return () => window.clearTimeout(t);
  }, [gameId, noKey]);

  // A new game or kind starts again at its first page; an answer for the one before is dropped.
  useEffect(() => {
    if (!source) return;
    const token = ++loadToken.current;
    setPage(null);
    setError(null);
    rpc<ArtPage>("art.list", { libraryId: gameId, sgdbGameId: source.id, kind, page: 0 })
      .then((r) => token === loadToken.current && setPage({ ...r, next: 1 }))
      .catch((e: unknown) => token === loadToken.current && setError(message(e)));
  }, [gameId, source, kind]);

  function openChoosing() {
    setChoosing(true);
    window.setTimeout(focusDefault);
  }

  function closeChoosing() {
    setChoosing(false);
    focusKind(state.current.kind);
  }

  function switchKind(next: ArtKind) {
    setKind(next);
    focusKind(next);
  }

  // The panel owns the controller while open: LB and RB switch kinds, B leaves the game list and then the panel.
  useEffect(
    () =>
      onAction((a) => {
        if (a === "back") {
          if (state.current.choosing) closeChoosing();
          else closeArtPicker();
          return true;
        }
        if (a === "lb" || a === "rb") {
          const i = KINDS.findIndex((k) => k.kind === state.current.kind);
          switchKind(KINDS[(i + (a === "rb" ? 1 : KINDS.length - 1)) % KINDS.length].kind);
          return true;
        }
        if (a === "guide") {
          closeArtPicker();
          return false;
        }
        return true;
      }),
    [],
  );

  async function request<T>(key: NonNullable<typeof busy>, work: () => Promise<T>) {
    if (busy !== null) return;
    setBusy(key);
    try {
      return await work();
    } catch (e) {
      showToast(message(e));
    } finally {
      setBusy(null);
    }
  }

  const applied = (r: ArtView) => {
    setImages(gameId, r.images);
    setPicked(r.picked);
  };

  const pick = (image: ArtImage) =>
    request(image.id, async () => {
      applied(await rpc<ArtView>("art.set", { libraryId: gameId, kind, imageId: image.id, url: image.url }));
      showToast(`${labelOf(kind)} changed`);
    });

  const reset = () =>
    request("reset", async () => {
      applied(await rpc<ArtView>("art.reset", { libraryId: gameId, kind }));
      showToast(`${labelOf(kind)} is back to the default`);
      focusKind(kind);
    });

  const loadMore = () => {
    if (!page || !source) return;
    const token = loadToken.current;
    void request("more", async () => {
      const r = await rpc<ArtPage>("art.list", { libraryId: gameId, sgdbGameId: source.id, kind, page: page.next });
      if (token === loadToken.current) setPage({ images: [...page.images, ...r.images], more: r.more, next: page.next + 1 });
    });
  };

  const search = () =>
    request("search", async () => {
      const r = await rpc<ArtSearchView>("art.search", { libraryId: gameId, query });
      setSources(r.games);
      window.setTimeout(() => focusEl(document.querySelector<HTMLElement>(".art-source")));
    });

  const chooseSource = (s: ArtSource) => {
    setSource(s);
    closeChoosing();
  };

  return (
    <div className="art-layer" role="dialog" aria-modal="true" aria-label={`Artwork for ${game.title}`}>
      <div className="art-scrim" onClick={closeArtPicker} />
      <section className="art-panel">
        <header className="art-head">
          <div className="art-head-text">
            <span className="kicker">Artwork · SteamGridDB</span>
            <h2 className="art-title">{game.title}</h2>
            {source && !choosing && (
              <p className="art-source-line">
                Images of {source.name}
                {source.year ? ` (${source.year})` : ""}
              </p>
            )}
          </div>
          {!noKey && (
            <div data-nav-group="art-match">
              <button
                className="btn btn-outline"
                data-nav
                data-kind="art-match"
                aria-expanded={choosing}
                disabled={sources === null && !error}
                onClick={() => (choosing ? closeChoosing() : openChoosing())}
              >
                <Search aria-hidden /> {choosing ? "Back to images" : "Not this game?"}
              </button>
            </div>
          )}
        </header>

        {noKey ? (
          <div className="art-empty">
            <ImageOff aria-hidden />
            <p>Add a SteamGridDB API key in Settings to change this game's artwork.</p>
          </div>
        ) : choosing ? (
          <div className="art-choose">
            <form
              className="art-search"
              data-nav-group="art-search"
              onSubmit={(e) => {
                e.preventDefault();
                void search();
              }}
            >
              <input
                className="art-input"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="Search SteamGridDB"
                aria-label="Search SteamGridDB"
                data-nav
                data-nav-default
                data-kind="field"
              />
              <button type="submit" className="btn btn-primary" data-nav data-kind="art-search" disabled={busy === "search" || !query.trim()}>
                Search
              </button>
            </form>
            <div className="art-sources" data-scroll="y" data-nav-group="art-sources">
              {sources?.length === 0 && <p className="art-note">SteamGridDB has no game called that. Try another name.</p>}
              {sources?.map((s) => (
                <button
                  key={s.id}
                  className="art-source"
                  data-nav
                  data-kind="art-source"
                  aria-current={s.id === source?.id ? "true" : undefined}
                  onClick={() => chooseSource(s)}
                >
                  <span className="art-source-name">{s.name}</span>
                  {s.year && <span className="art-source-year num">{s.year}</span>}
                  {s.id === source?.id && <Check aria-hidden />}
                </button>
              ))}
            </div>
          </div>
        ) : (
          <>
            <div className="art-bar">
              <div className="seg" role="tablist" aria-label="Artwork" data-nav-group="art-kinds" data-nav-entry="selected">
                {KINDS.map((k) => (
                  <button
                    key={k.kind}
                    role="tab"
                    className="seg-btn"
                    aria-selected={kind === k.kind}
                    data-nav
                    data-nav-default={kind === k.kind ? "" : undefined}
                    data-kind="art-kind"
                    data-art-kind={k.kind}
                    onClick={() => switchKind(k.kind)}
                  >
                    {k.label}
                    {picked[k.kind] !== undefined && <span className="tab-dot" aria-label="(changed)" />}
                  </button>
                ))}
              </div>
              <div data-nav-group="art-reset">
                <button
                  className="btn btn-outline"
                  data-nav
                  data-kind="art-reset"
                  disabled={picked[kind] === undefined || busy === "reset"}
                  onClick={reset}
                >
                  <RotateCcw aria-hidden /> Use default
                </button>
              </div>
            </div>

            {error ? (
              <div className="art-empty">
                <ImageOff aria-hidden />
                <p>{error}</p>
              </div>
            ) : !page ? (
              <div className="art-grid" data-art={kind} aria-busy="true">
                {Array.from({ length: kind === "portrait" ? 12 : 6 }, (_, i) => (
                  <div key={i} className="art-tile art-placeholder" />
                ))}
              </div>
            ) : page.images.length === 0 ? (
              <div className="art-empty">
                <ImageOff aria-hidden />
                <p>
                  SteamGridDB has no {labelOf(kind).toLowerCase()} for {source?.name ?? game.title} yet.
                </p>
              </div>
            ) : (
              <div className="art-grid" data-art={kind} data-scroll="y" data-scroll-center data-nav-group="art-images">
                {page.images.map((image) => {
                  const inUse = picked[kind] === image.id;
                  return (
                    <button
                      key={image.id}
                      className="art-tile"
                      data-nav
                      data-kind="art-image"
                      aria-pressed={inUse}
                      aria-busy={busy === image.id}
                      aria-label={`${labelOf(kind)}${image.style ? `, ${image.style}` : ""}${image.width ? `, ${image.width}×${image.height}` : ""}${inUse ? ", in use" : ""}`}
                      onClick={() => void pick(image)}
                    >
                      <img
                        src={image.thumb}
                        alt=""
                        loading="lazy"
                        decoding="async"
                        onLoad={(e) => e.currentTarget.setAttribute("data-loaded", "")}
                      />
                      {inUse && (
                        <span className="art-badge">
                          <Check aria-hidden /> In use
                        </span>
                      )}
                    </button>
                  );
                })}
                {page.more && (
                  <div className="art-more">
                    <button className="btn btn-outline" data-nav data-kind="art-more" disabled={busy === "more"} onClick={loadMore}>
                      {busy === "more" ? "Loading…" : "Show more"}
                    </button>
                  </div>
                )}
              </div>
            )}
          </>
        )}

        <footer className="hints art-hints">
          <Hint glyph="A">Select</Hint>
          {!choosing && !noKey && <Hint glyph="LB">Artwork type</Hint>}
          <Hint glyph="B">{choosing ? "Back" : "Close"}</Hint>
        </footer>
      </section>
    </div>
  );
}
