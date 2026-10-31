// The Windows app has the same colors (src/Launcher/ui/src/data/platforms.ts): change both together.

/** Brand colors per platform from the design tokens. */
const PlatformColors: Record<string, string> = {
  steam: "#e07a0f",
  // GOG's purple, toward magenta so it doesn't read as PlayStation's violet.
  gog: "#a3369f",
  // Epic's brand is black and white, which a chart can't show on both themes.
  epic: "#64748b",
  psn: "#7c3aed",
  playstation: "#7c3aed",
  ps5: "#7c3aed",
  ps4: "#7c3aed",
  ps3: "#7c3aed",
  pc: "#0f7491",
  playnite: "#0f7491",
  xbox: "#2f9e44",
  switch: "#e0455a",
  switch2: "#e0455a",
  nintendo: "#e0455a",
  manual: "#0a9a6c",
  custom: "#0a9a6c",
};

/** Color for a platform with no key or name at all. */
const UnknownColor = "#8a96a3";

/** Fallback palette for platforms without a brand color (user-defined manual platforms). */
const FallbackColors = ["#0f7491", "#0a9a6c", "#b9650a", "#3b7f99", "#7c3aed", "#e0455a"];

/**
 * Resolves a platform's chart color from its key and, failing that, its display name.
 * Unknown platforms get a stable color from the fallback palette.
 */
export function platformColor(key: string | null | undefined, name = ""): string {
  const normalizedKey = (key ?? "").trim().toLowerCase();
  if (!normalizedKey && !name.trim()) return UnknownColor;
  if (PlatformColors[normalizedKey]) return PlatformColors[normalizedKey];

  const haystack = `${normalizedKey} ${name.toLowerCase()}`;
  for (const [match, color] of Object.entries(PlatformColors)) {
    if (haystack.includes(match)) return color;
  }

  let hash = 0;
  for (const char of haystack) hash = (hash * 31 + char.charCodeAt(0)) >>> 0;
  return FallbackColors[hash % FallbackColors.length];
}
