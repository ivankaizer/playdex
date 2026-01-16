import { platformLabels, providerNames } from "../constants";
import type {
  AccountSession,
  ManualInput,
  ManualMappingSearchResult,
} from "../types";

export const formatPlatformLabel = (platform: string) =>
  platformLabels[platform] ?? platform;

export const formatProviderName = (platform: string) =>
  providerNames[platform] ?? platform;

export const formatSessionPlatformLabel = (session: AccountSession) => {
  const platform = session.mappedPlatform ?? session.platform;
  return formatPlatformLabel(platform);
};

export const formatStatsPlatformLabel = (platform: string) =>
  formatPlatformLabel(platform);

export const formatManualMappingType = (source?: "local" | "igdb" | null) =>
  source === "igdb" ? "IGDB" : "Local";

export const formatManualInputLabel = (input: ManualInput) => {
  if (!input.mappedGame) {
    return input.input;
  }
  const platformLabel = input.mappedGame.platform
    ? formatPlatformLabel(input.mappedGame.platform)
    : "Unknown platform";
  const typeLabel = formatManualMappingType(input.mappedSource);
  return `${platformLabel} · ${typeLabel} · ${input.mappedGame.title}`;
};

export const formatEntryResultLabel = (result: ManualMappingSearchResult) => {
  if (result.source === "local") {
    const platformLabel = result.platform
      ? formatPlatformLabel(result.platform)
      : "Unknown platform";
    return `${result.title} · ${platformLabel}`;
  }
  const platformLabel = result.platform ?? "Platform not set";
  return `${result.title} · ${platformLabel} · IGDB`;
};

export const formatDateLabel = (date: Date) =>
  date.toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
  });

export const formatTimeLabel = (date: Date) =>
  date.toLocaleTimeString("en-US", { hour: "numeric", minute: "2-digit" });

export const formatDuration = (seconds: number, format: "short" | "long") => {
  const safeSeconds = Math.max(0, Math.floor(seconds));
  const hours = Math.floor(safeSeconds / 3600);
  const minutes = Math.floor((safeSeconds % 3600) / 60);
  if (hours <= 0 && minutes <= 0) {
    return format === "short" ? "0m" : "0 min";
  }
  if (format === "short") {
    if (hours > 0) {
      return `${hours}h ${String(minutes).padStart(2, "0")}m`;
    }
    return `${minutes}m`;
  }
  if (hours > 0) {
    return `${hours}h ${minutes}m`;
  }
  return `${minutes} min`;
};

export const formatHours = (seconds: number, decimals: number) => {
  const hours = seconds / 3600;
  const value =
    decimals === 0
      ? Math.round(hours)
      : Number.isFinite(hours)
        ? Number(hours.toFixed(decimals))
        : 0;
  return `${value}h`;
};

export const formatUtcDateInput = (date: Date) =>
  date.toISOString().slice(0, 10);
