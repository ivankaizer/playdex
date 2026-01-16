import type { NavItem } from "./types";

export const protectedPaths = new Set([
  "/dashboard",
  "/sessions",
  "/mapping",
  "/settings",
]);

export const navItems: NavItem[] = [
  { label: "Dashboard", path: "/dashboard" },
  { label: "Sessions", path: "/sessions" },
  { label: "Mapping", path: "/mapping" },
];

export const buildGamePath = (title: string) =>
  `/games/${encodeURIComponent(title)}`;

export const platformOptions = [
  { key: "ps5", label: "PS5" },
  { key: "pc", label: "PC" },
  { key: "steam", label: "Steam" },
  { key: "playnite", label: "Playnite" },
  { key: "switch-1", label: "Nintendo Switch 1" },
  { key: "switch-2", label: "Nintendo Switch 2" },
  { key: "xbox-series-xs", label: "Xbox Series X|S" },
  { key: "unknown", label: "Unknown" },
];

export const manualPlatformOptions = [
  { key: "auto", label: "Use IGDB platform" },
  ...platformOptions,
];

export const capsuleUrl =
  "https://shared.steamstatic.com/store_item_assets/steam/apps/2186680/library_600x900_2x.jpg";

export const platformLabels: Record<string, string> = {
  ps5: "PS5",
  "xbox-series-xs": "Xbox Series X|S",
  pc: "PC",
  "switch-1": "Nintendo Switch 1",
  "switch-2": "Nintendo Switch 2",
  steam: "Steam",
  psn: "PS5",
  playnite: "Playnite",
  manual: "Manual",
  unknown: "Unknown",
};

export const providerNames: Record<string, string> = {
  steam: "Steam",
  psn: "PlayStation",
  playnite: "Playnite",
  manual: "Manual",
};

export const providerMeta: Record<
  string,
  { label: string; color: string; accent: string }
> = {
  steam: { label: "S", color: "bg-slate-800", accent: "text-slate-100" },
  psn: { label: "P", color: "bg-indigo-500/30", accent: "text-indigo-200" },
  playnite: {
    label: "PL",
    color: "bg-amber-500/30",
    accent: "text-amber-200",
  },
  manual: {
    label: "M",
    color: "bg-teal-500/30",
    accent: "text-teal-200",
  },
};
