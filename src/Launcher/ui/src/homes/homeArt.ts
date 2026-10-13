import { createStore } from "../nav/store";

/*
 * How Home's game cards show their wide art. On this PC that art is Steam's cached header (460 x 215, about 2.14:1),
 * wider than Steam's capsule shape (616 x 353) the cards had, which cut its sides off. Whole gives the cards the
 * header's shape; Hero + logo uses the hero art, made to be cropped. A per-screen preference (Settings), kept in this
 * browser profile; ?art=<id> in the page URL overrides it.
 */
export type HomeArt = "whole" | "hero";

export const HOME_ART: { id: HomeArt; label: string; note: string }[] = [
  { id: "whole", label: "Whole", note: "The art's own wide shape, nothing cut." },
  { id: "hero", label: "Hero + logo", note: "The game's hero art with its logo on top, like the large card." },
];

const KEY = "playdex.homeArt";
const isHomeArt = (v: unknown): v is HomeArt => HOME_ART.some((a) => a.id === v);

function initial(): HomeArt {
  const fromUrl = new URLSearchParams(location.search).get("art");
  if (isHomeArt(fromUrl)) return fromUrl;
  try {
    const saved = localStorage.getItem(KEY);
    if (isHomeArt(saved)) return saved;
  } catch {
    // Storage blocked: the default for this session.
  }
  return "whole";
}

export const homeArt = createStore<HomeArt>(initial());

export function setHomeArt(art: HomeArt) {
  homeArt.set(art);
  try {
    localStorage.setItem(KEY, art);
  } catch {
    // Not remembered; still applies now.
  }
}

/** The next choice, for a single button that cycles through them (Big Picture). */
export const nextHomeArt = (art: HomeArt) => HOME_ART[(HOME_ART.findIndex((a) => a.id === art) + 1) % HOME_ART.length].id;
