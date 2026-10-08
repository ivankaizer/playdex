import type { GameSummary, GameUpdate, IntegrationSummary, IntegrationUpdate } from "./api";
import { shiftHours } from "./time";

export const SessionStatusOptions = [
  { label: "Live", value: "live" },
  { label: "Ended", value: "ended" },
] as const;

export const GenerationStatuses = ["errors", "success", "failure", "timeout", "cancelled", "running"] as const;

export const DashboardRefreshMs = 30_000;

/** Two-letter initials from an email address, as shown in the sidebar user chip. */
export function userInitials(email: string): string {
  const parts = email.split(/[@.-]/).filter((part) => part.length > 0);
  if (parts.length >= 2) return `${parts[0]![0]!.toUpperCase()}${parts[1]![0]!.toUpperCase()}`;
  return email.length > 0 ? email[0]!.toUpperCase() : "";
}

export function sessionsTodayLink(dayStart: string, dayEnd: string): string {
  return `/admin/sessions?from=${encodeURIComponent(dayStart)}&to=${encodeURIComponent(dayEnd)}`;
}

export function aiErrorsLink(updatedAt: string): string {
  return `/admin/ai-generations?status=errors&since=${encodeURIComponent(shiftHours(updatedAt, -24))}&until=${encodeURIComponent(updatedAt)}`;
}

export function generationLink(id: string): string {
  return `/admin/ai-generations?id=${encodeURIComponent(id)}`;
}

export function sourceLabel(source: string): string {
  if (source === "api") return "API";
  return source.length > 0 ? source[0]!.toUpperCase() + source.slice(1) : source;
}

/** Client-side page of a list (the Blazor grids paged locally). */
export function pageOf<T>(items: readonly T[], page: number, size: number): { rows: T[]; page: number; pages: number } {
  const pages = Math.max(1, Math.ceil(items.length / size));
  const current = Math.min(Math.max(0, page), pages - 1);
  return { rows: items.slice(current * size, current * size + size), page: current, pages };
}

export function hasNextPage(page: number, pageSize: number, total: number): boolean {
  return (page + 1) * pageSize < total;
}

export interface GameForm {
  title: string;
  capsuleUrl: string | null;
  titleLocked: boolean;
  capsuleUrlLocked: boolean;
}

export function gameFormFor(game: GameSummary): GameForm {
  return {
    title: game.title,
    capsuleUrl: game.capsuleUrl,
    titleLocked: game.titleLocked,
    capsuleUrlLocked: game.capsuleUrlLocked,
  };
}

/**
 * Same intent the Blazor page sent: a field counts as changed only when its value differs from the
 * loaded game, and the lock checkboxes are always sent explicitly.
 */
export function gameUpdateFor(game: GameSummary, form: GameForm): GameUpdate {
  return {
    title: form.title,
    titleChanged: form.title !== game.title,
    capsuleUrl: form.capsuleUrl,
    capsuleUrlChanged: form.capsuleUrl !== game.capsuleUrl,
    titleLocked: form.titleLocked,
    capsuleUrlLocked: form.capsuleUrlLocked,
  };
}

export interface CredentialForm {
  replaceApiKey: boolean;
  apiKey: string;
  changeAccountId: boolean;
  accountId: string;
}

export function credentialFormFor(integration: IntegrationSummary): CredentialForm {
  return {
    replaceApiKey: false,
    apiKey: integration.apiKey ?? "",
    changeAccountId: false,
    accountId: integration.accountId ?? "",
  };
}

/** Null when nothing was selected for change (the save is a no-op, like the Blazor page). */
export function integrationUpdateFor(form: CredentialForm): IntegrationUpdate | null {
  if (!form.replaceApiKey && !form.changeAccountId) return null;
  return {
    apiKeyChanged: form.replaceApiKey,
    apiKey: form.replaceApiKey && form.apiKey !== "" ? form.apiKey : null,
    accountIdChanged: form.changeAccountId,
    accountId: form.changeAccountId ? form.accountId : null,
  };
}

export function integrationLabel(integration: IntegrationSummary): string {
  return `${integration.accountId ?? "(no account)"} · ${integration.platform} · ${integration.email}`;
}

export function mergeValidationError(sourceId: number | null, targetId: number | null): string | null {
  return sourceId === null || targetId === null || sourceId === targetId ? "Select two different games." : null;
}
