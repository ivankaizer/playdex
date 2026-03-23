import { useEffect, useMemo, useState } from "react";

import { apiRequest } from "../api";
import { navItems as defaultNavItems } from "../constants";
import { AppHeader } from "../components/AppHeader";
import { Background } from "../components/Background";
import type {
  AdminCredential,
  AdminCredentialUpdateResponse,
  AdminCredentialsResponse,
  AdminGame,
  AdminGameSearchResponse,
  AdminGameUpdateResponse,
  AdminMovableSession,
  AdminPlatformStatus,
  AdminPlatformStatusesResponse,
  AdminPlatformStatusUpdateResponse,
  AdminRecentSessionsResponse,
  AdminSessionMoveResponse,
  AdminSessionsListResponse,
  NavItem,
} from "../types";
import { formatDuration, formatProviderName } from "../utils/formatters";

type AdminPageProps = {
  path: string;
  navItems?: NavItem[];
  onNavigate: (path: string) => void;
  onLogout: () => void;
  theme: "light" | "dark";
  onToggleTheme: () => void;
};

type CredentialDraft = {
  apiKey: string;
  user: string;
};

type GameDraft = {
  title: string;
  lockTitle: boolean;
  imageUrl: string;
  lockImage: boolean;
};

const SESSION_PAGE_SIZE = 25;

export const AdminPage = ({
  path,
  navItems = defaultNavItems,
  onNavigate,
  onLogout,
  theme,
  onToggleTheme,
}: AdminPageProps) => {
  const [recentUsers, setRecentUsers] = useState<
    AdminRecentSessionsResponse["users"]
  >([]);
  const [isSessionsLoading, setIsSessionsLoading] = useState(false);
  const [sessionsError, setSessionsError] = useState("");

  const [platforms, setPlatforms] = useState<AdminPlatformStatus[]>([]);
  const [isPlatformsLoading, setIsPlatformsLoading] = useState(false);
  const [platformsError, setPlatformsError] = useState("");
  const [updatingPlatforms, setUpdatingPlatforms] = useState<string[]>([]);

  const [credentials, setCredentials] = useState<AdminCredential[]>([]);
  const [credentialDrafts, setCredentialDrafts] = useState<
    Record<number, CredentialDraft>
  >({});
  const [isCredentialsLoading, setIsCredentialsLoading] = useState(false);
  const [credentialsError, setCredentialsError] = useState("");
  const [savingCredentialIds, setSavingCredentialIds] = useState<number[]>([]);
  const [selectedCredentialPlatform, setSelectedCredentialPlatform] =
    useState("");

  const [sessionQuery, setSessionQuery] = useState("");
  const [movableSessions, setMovableSessions] = useState<AdminMovableSession[]>(
    []
  );
  const [sessionTotal, setSessionTotal] = useState(0);
  const [sessionPage, setSessionPage] = useState(1);
  const [selectedSessionIds, setSelectedSessionIds] = useState<number[]>([]);
  const [targetCredentialId, setTargetCredentialId] = useState("");
  const [isSessionListLoading, setIsSessionListLoading] = useState(false);
  const [isMovingSessions, setIsMovingSessions] = useState(false);
  const [moveSessionsError, setMoveSessionsError] = useState("");
  const [moveSessionsNotice, setMoveSessionsNotice] = useState("");

  const [gameQuery, setGameQuery] = useState("");
  const [games, setGames] = useState<AdminGame[]>([]);
  const [gameDrafts, setGameDrafts] = useState<Record<string, GameDraft>>({});
  const [hasGameSearch, setHasGameSearch] = useState(false);
  const [isGamesLoading, setIsGamesLoading] = useState(false);
  const [gamesError, setGamesError] = useState("");
  const [savingGameIds, setSavingGameIds] = useState<string[]>([]);

  const credentialPlatforms = useMemo(
    () =>
      Array.from(new Set(credentials.map((credential) => credential.platform))),
    [credentials]
  );

  const credentialCountsByPlatform = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const credential of credentials) {
      counts[credential.platform] = (counts[credential.platform] ?? 0) + 1;
    }
    return counts;
  }, [credentials]);

  const filteredCredentials = useMemo(() => {
    if (!selectedCredentialPlatform) {
      return credentials;
    }
    return credentials.filter(
      (credential) => credential.platform === selectedCredentialPlatform
    );
  }, [credentials, selectedCredentialPlatform]);

  const allVisibleSessionsSelected =
    movableSessions.length > 0 &&
    movableSessions.every((session) =>
      selectedSessionIds.includes(session.sessionId)
    );

  const sessionPageCount = Math.max(
    1,
    Math.ceil(sessionTotal / SESSION_PAGE_SIZE)
  );
  const sessionRangeStart =
    sessionTotal === 0 ? 0 : (sessionPage - 1) * SESSION_PAGE_SIZE + 1;
  const sessionRangeEnd =
    sessionTotal === 0 || movableSessions.length === 0
      ? 0
      : sessionRangeStart + movableSessions.length - 1;

  useEffect(() => {
    let cancelled = false;
    setIsSessionsLoading(true);
    setSessionsError("");
    setIsPlatformsLoading(true);
    setPlatformsError("");
    setIsCredentialsLoading(true);
    setCredentialsError("");
    setIsSessionListLoading(true);
    setMoveSessionsError("");
    setMoveSessionsNotice("");

    const loadAll = async () => {
      try {
        const [sessionsData, platformData, credentialsData, movableData] =
          await Promise.all([
            apiRequest<AdminRecentSessionsResponse>("/admin/sessions/recent"),
            apiRequest<AdminPlatformStatusesResponse>(
              "/admin/providers/platforms"
            ),
            apiRequest<AdminCredentialsResponse>("/admin/credentials"),
            apiRequest<AdminSessionsListResponse>(
              `/admin/sessions?limit=${SESSION_PAGE_SIZE}&offset=0`
            ),
          ]);

        if (cancelled) {
          return;
        }

        setRecentUsers(sessionsData.users);
        setPlatforms(platformData.platforms);
        setCredentials(credentialsData.credentials);
        setMovableSessions(movableData.sessions);
        setSessionTotal(movableData.total);
        setSessionPage(1);
        setSelectedSessionIds([]);
        setCredentialDrafts(
          credentialsData.credentials.reduce<Record<number, CredentialDraft>>(
            (acc, credential) => {
              acc[credential.id] = {
                apiKey: credential.apiKey ?? "",
                user: credential.user ?? "",
              };
              return acc;
            },
            {}
          )
        );
        setIsSessionsLoading(false);
        setIsPlatformsLoading(false);
        setIsCredentialsLoading(false);
        setIsSessionListLoading(false);
      } catch (error) {
        if (cancelled) {
          return;
        }
        const message =
          error instanceof Error ? error.message : "Failed to load admin data.";
        setSessionsError(message);
        setPlatformsError(message);
        setCredentialsError(message);
        setMoveSessionsError(message);
        setIsSessionsLoading(false);
        setIsPlatformsLoading(false);
        setIsCredentialsLoading(false);
        setIsSessionListLoading(false);
      }
    };

    loadAll();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (credentialPlatforms.length === 0) {
      if (selectedCredentialPlatform) {
        setSelectedCredentialPlatform("");
      }
      return;
    }

    if (!selectedCredentialPlatform) {
      setSelectedCredentialPlatform(credentialPlatforms[0]);
      return;
    }

    if (!credentialPlatforms.includes(selectedCredentialPlatform)) {
      setSelectedCredentialPlatform(credentialPlatforms[0]);
    }
  }, [credentialPlatforms, selectedCredentialPlatform]);

  const handlePlatformToggle = async (platform: AdminPlatformStatus) => {
    const nextEnabled = !platform.enabled;
    setUpdatingPlatforms((current) =>
      current.includes(platform.platform)
        ? current
        : [...current, platform.platform]
    );
    setPlatformsError("");

    try {
      const response = await apiRequest<AdminPlatformStatusUpdateResponse>(
        "/admin/providers/platforms/status",
        {
          method: "PUT",
          body: {
            platform: platform.platform,
            enabled: nextEnabled,
          },
        }
      );
      setPlatforms((current) =>
        current.map((entry) =>
          entry.platform === response.platform
            ? {
                ...entry,
                enabled: response.enabled,
                activeCredentials: response.enabled
                  ? entry.totalCredentials
                  : 0,
              }
            : entry
        )
      );
    } catch (error) {
      setPlatformsError(
        error instanceof Error
          ? error.message
          : "Failed to update platform status."
      );
    } finally {
      setUpdatingPlatforms((current) =>
        current.filter((entry) => entry !== platform.platform)
      );
    }
  };

  const handleCredentialDraftChange = (
    credentialId: number,
    field: keyof CredentialDraft,
    value: string
  ) => {
    setCredentialDrafts((current) => ({
      ...current,
      [credentialId]: {
        ...(current[credentialId] ?? { apiKey: "", user: "" }),
        [field]: value,
      },
    }));
  };

  const handleCredentialSave = async (credentialId: number) => {
    const draft = credentialDrafts[credentialId];
    if (!draft) {
      return;
    }

    setSavingCredentialIds((current) =>
      current.includes(credentialId) ? current : [...current, credentialId]
    );
    setCredentialsError("");

    try {
      const response = await apiRequest<AdminCredentialUpdateResponse>(
        `/admin/credentials/${credentialId}`,
        {
          method: "PUT",
          body: {
            apiKey: draft.apiKey.trim() || null,
            user: draft.user.trim() || null,
          },
        }
      );
      setCredentials((current) =>
        current.map((entry) =>
          entry.id === credentialId ? response.credential : entry
        )
      );
      setCredentialDrafts((current) => ({
        ...current,
        [credentialId]: {
          apiKey: response.credential.apiKey ?? "",
          user: response.credential.user ?? "",
        },
      }));
    } catch (error) {
      setCredentialsError(
        error instanceof Error ? error.message : "Failed to update credential."
      );
    } finally {
      setSavingCredentialIds((current) =>
        current.filter((entry) => entry !== credentialId)
      );
    }
  };

  const loadMovableSessions = async (query: string, page: number) => {
    setIsSessionListLoading(true);
    setMoveSessionsError("");
    const safePage = Math.max(1, Math.floor(page));
    const offset = (safePage - 1) * SESSION_PAGE_SIZE;
    try {
      const response = await apiRequest<AdminSessionsListResponse>(
        `/admin/sessions?query=${encodeURIComponent(
          query
        )}&limit=${SESSION_PAGE_SIZE}&offset=${offset}`
      );
      setMovableSessions(response.sessions);
      setSessionTotal(response.total);
      setSessionPage(safePage);
      setSelectedSessionIds((current) =>
        current.filter((sessionId) =>
          response.sessions.some((session) => session.sessionId === sessionId)
        )
      );
      setIsSessionListLoading(false);
    } catch (error) {
      setIsSessionListLoading(false);
      setMoveSessionsError(
        error instanceof Error ? error.message : "Failed to load sessions."
      );
    }
  };

  const handleSessionSearch = () => {
    setMoveSessionsNotice("");
    void loadMovableSessions(sessionQuery.trim(), 1);
  };

  const toggleSessionSelection = (sessionId: number) => {
    setSelectedSessionIds((current) =>
      current.includes(sessionId)
        ? current.filter((id) => id !== sessionId)
        : [...current, sessionId]
    );
  };

  const toggleSelectAllVisibleSessions = () => {
    if (allVisibleSessionsSelected) {
      setSelectedSessionIds((current) =>
        current.filter(
          (sessionId) =>
            !movableSessions.some((session) => session.sessionId === sessionId)
        )
      );
      return;
    }

    setSelectedSessionIds((current) => {
      const combined = new Set(current);
      for (const session of movableSessions) {
        combined.add(session.sessionId);
      }
      return Array.from(combined);
    });
  };

  const handleMoveSessions = async () => {
    const parsedTarget = Number(targetCredentialId);
    if (!Number.isInteger(parsedTarget) || parsedTarget <= 0) {
      setMoveSessionsError("Select a target account.");
      return;
    }
    if (selectedSessionIds.length === 0) {
      setMoveSessionsError("Select at least one session.");
      return;
    }

    setIsMovingSessions(true);
    setMoveSessionsError("");
    setMoveSessionsNotice("");

    try {
      const response = await apiRequest<AdminSessionMoveResponse>(
        "/admin/sessions/move",
        {
          method: "PUT",
          body: {
            sessionIds: selectedSessionIds,
            targetCredentialId: parsedTarget,
          },
        }
      );

      setMoveSessionsNotice(`Moved ${response.movedSessions} session(s).`);
      setSelectedSessionIds([]);
      await loadMovableSessions(sessionQuery.trim(), sessionPage);
    } catch (error) {
      setMoveSessionsError(
        error instanceof Error ? error.message : "Failed to move sessions."
      );
    } finally {
      setIsMovingSessions(false);
    }
  };

  const handleSessionPageChange = (nextPage: number) => {
    if (nextPage < 1 || nextPage > sessionPageCount || isSessionListLoading) {
      return;
    }
    void loadMovableSessions(sessionQuery.trim(), nextPage);
  };

  const runGameSearch = async (query: string) => {
    setIsGamesLoading(true);
    setGamesError("");
    try {
      const response = await apiRequest<AdminGameSearchResponse>(
        `/admin/games/search?query=${encodeURIComponent(query)}&limit=20`
      );
      setGames(response.games);
      setGameDrafts(
        response.games.reduce<Record<string, GameDraft>>((acc, game) => {
          acc[game.id] = {
            title: game.title,
            lockTitle: game.titleLocked,
            imageUrl: game.imageUrl ?? "",
            lockImage: game.imageLocked,
          };
          return acc;
        }, {})
      );
      setIsGamesLoading(false);
    } catch (error) {
      setIsGamesLoading(false);
      setGamesError(
        error instanceof Error ? error.message : "Failed to search games."
      );
    }
  };

  const handleGameSearch = () => {
    const trimmedQuery = gameQuery.trim();
    if (!trimmedQuery) {
      setHasGameSearch(false);
      setGames([]);
      setGameDrafts({});
      setGamesError("Enter a game title to search.");
      return;
    }

    setHasGameSearch(true);
    void runGameSearch(trimmedQuery);
  };

  const handleGameDraftChange = (
    gameId: string,
    field: keyof GameDraft,
    value: string | boolean
  ) => {
    setGameDrafts((current) => ({
      ...current,
      [gameId]: {
        ...(current[gameId] ?? {
          title: "",
          lockTitle: true,
          imageUrl: "",
          lockImage: true,
        }),
        [field]: value,
      },
    }));
  };

  const handleGameSave = async (gameId: string) => {
    const draft = gameDrafts[gameId];
    if (!draft) {
      return;
    }

    setSavingGameIds((current) =>
      current.includes(gameId) ? current : [...current, gameId]
    );
    setGamesError("");

    try {
      const response = await apiRequest<AdminGameUpdateResponse>(
        `/admin/games?gameId=${encodeURIComponent(gameId)}`,
        {
          method: "PUT",
          body: {
            gameId,
            id: gameId,
            title: draft.title.trim(),
            lockTitle: draft.lockTitle,
            imageUrl: draft.imageUrl.trim() || null,
            lockImage: draft.lockImage,
          },
        }
      );
      setGames((current) =>
        current.map((entry) => (entry.id === gameId ? response.game : entry))
      );
      setGameDrafts((current) => ({
        ...current,
        [gameId]: {
          title: response.game.title,
          lockTitle: response.game.titleLocked,
          imageUrl: response.game.imageUrl ?? "",
          lockImage: response.game.imageLocked,
        },
      }));
    } catch (error) {
      setGamesError(
        error instanceof Error ? error.message : "Failed to update game."
      );
    } finally {
      setSavingGameIds((current) =>
        current.filter((entry) => entry !== gameId)
      );
    }
  };

  return (
    <main className="relative min-h-screen overflow-hidden bg-slate-200 dark:bg-slate-950 text-slate-900 dark:text-slate-100">
      <Background />
      <div className="relative mx-auto w-full max-w-6xl p-3 sm:px-6 sm:py-12">
        <AppHeader
          title="Admin"
          navItems={navItems}
          path={path}
          onNavigate={onNavigate}
          onLogout={onLogout}
          theme={theme}
          onToggleTheme={onToggleTheme}
        />

        <section className="mt-10 grid gap-6 lg:grid-cols-2">
          <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
            <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
              Recent sessions
            </p>
            <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
              Last 3 sessions per user
            </h2>
            {sessionsError ? (
              <p className="mt-4 text-xs text-rose-700 dark:text-rose-300">
                {sessionsError}
              </p>
            ) : null}
            <div className="mt-5 space-y-4">
              {isSessionsLoading ? (
                <p className="text-xs text-slate-500 dark:text-slate-400">
                  Loading sessions...
                </p>
              ) : recentUsers.length === 0 ? (
                <p className="text-xs text-slate-500 dark:text-slate-400">
                  No sessions found.
                </p>
              ) : (
                recentUsers.map((user) => (
                  <div
                    key={user.userId}
                    className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/40 dark:bg-slate-950/40 p-3"
                  >
                    <p className="text-sm font-semibold text-slate-900 dark:text-white">
                      {user.email}
                    </p>
                    <div className="mt-2 space-y-2">
                      {user.sessions.map((session) => (
                        <div
                          key={session.sessionId}
                          className="flex items-start justify-between gap-3 rounded-xl border border-slate-200 dark:border-slate-800 bg-white/70 dark:bg-slate-900/70 px-3 py-2 text-xs"
                        >
                          <div className="min-w-0">
                            <p className="truncate font-medium text-slate-800 dark:text-slate-100">
                              {session.gameName}
                            </p>
                            <p className="mt-1 text-slate-500 dark:text-slate-400">
                              {new Date(session.startedAt).toLocaleString()} ·{" "}
                              {formatDuration(session.durationSeconds, "short")}
                            </p>
                          </div>
                          <span
                            className={`shrink-0 rounded-full px-2 py-1 font-semibold uppercase tracking-[0.2em] ${
                              session.live
                                ? "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300"
                                : "bg-slate-200 text-slate-600 dark:bg-slate-800 dark:text-slate-300"
                            }`}
                          >
                            {session.live ? "Live" : "Ended"}
                          </span>
                        </div>
                      ))}
                    </div>
                  </div>
                ))
              )}
            </div>
          </div>

          <div className="rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
            <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
              Provider controls
            </p>
            <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
              Platform-level enable toggle
            </h2>
            {platformsError ? (
              <p className="mt-4 text-xs text-rose-700 dark:text-rose-300">
                {platformsError}
              </p>
            ) : null}
            <div className="mt-5 space-y-3">
              {isPlatformsLoading ? (
                <p className="text-xs text-slate-500 dark:text-slate-400">
                  Loading platforms...
                </p>
              ) : platforms.length === 0 ? (
                <p className="text-xs text-slate-500 dark:text-slate-400">
                  No provider credentials found.
                </p>
              ) : (
                platforms.map((platform) => {
                  const isUpdating = updatingPlatforms.includes(
                    platform.platform
                  );
                  return (
                    <div
                      key={platform.platform}
                      className="flex items-center justify-between rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/40 dark:bg-slate-950/40 px-4 py-3"
                    >
                      <div>
                        <p className="text-sm font-semibold text-slate-900 dark:text-white">
                          {formatProviderName(platform.platform)}
                        </p>
                        <p className="text-xs text-slate-500 dark:text-slate-400">
                          {platform.activeCredentials}/
                          {platform.totalCredentials} active
                        </p>
                      </div>
                      <button
                        type="button"
                        onClick={() => {
                          void handlePlatformToggle(platform);
                        }}
                        disabled={isUpdating}
                        className={`rounded-full px-3 py-2 text-xs font-semibold uppercase tracking-[0.25em] ${
                          platform.enabled
                            ? "bg-emerald-400 text-slate-900"
                            : "border border-slate-300 dark:border-slate-700 text-slate-700 dark:text-slate-200"
                        } disabled:cursor-not-allowed disabled:opacity-60`}
                      >
                        {isUpdating
                          ? "Updating..."
                          : platform.enabled
                            ? "Enabled"
                            : "Disabled"}
                      </button>
                    </div>
                  );
                })
              )}
            </div>
          </div>
        </section>

        <section className="mt-6 rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
          <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
            Credentials
          </p>
          <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
            Update platform credential records
          </h2>
          {credentialsError ? (
            <p className="mt-4 text-xs text-rose-700 dark:text-rose-300">
              {credentialsError}
            </p>
          ) : null}
          {isCredentialsLoading ? (
            <p className="mt-5 text-xs text-slate-500 dark:text-slate-400">
              Loading credentials...
            </p>
          ) : credentials.length === 0 ? (
            <p className="mt-5 text-xs text-slate-500 dark:text-slate-400">
              No credentials found.
            </p>
          ) : (
            <>
              <div className="mt-5 flex flex-wrap gap-2">
                {credentialPlatforms.map((platform) => {
                  const isActive = selectedCredentialPlatform === platform;
                  return (
                    <button
                      key={platform}
                      type="button"
                      onClick={() => setSelectedCredentialPlatform(platform)}
                      className={`rounded-full px-3 py-2 text-xs font-semibold uppercase tracking-[0.25em] transition ${
                        isActive
                          ? "bg-emerald-400 text-slate-900"
                          : "border border-slate-300 dark:border-slate-700 text-slate-700 dark:text-slate-200 hover:border-emerald-400/60"
                      }`}
                    >
                      {formatProviderName(platform)} (
                      {credentialCountsByPlatform[platform] ?? 0})
                    </button>
                  );
                })}
              </div>
              <div className="mt-4 space-y-3">
                {filteredCredentials.map((credential) => {
                  const draft = credentialDrafts[credential.id] ?? {
                    apiKey: "",
                    user: "",
                  };
                  const isSaving = savingCredentialIds.includes(credential.id);
                  return (
                    <div
                      key={credential.id}
                      className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/40 dark:bg-slate-950/40 p-4"
                    >
                      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
                        <p className="text-sm font-semibold text-slate-900 dark:text-white">
                          {credential.userEmail} ·{" "}
                          {formatProviderName(credential.platform)}
                        </p>
                        <span className="text-xs text-slate-500 dark:text-slate-400">
                          ID {credential.id}
                        </span>
                      </div>
                      <div className="grid gap-3 md:grid-cols-2">
                        <label className="text-xs text-slate-600 dark:text-slate-300">
                          API key
                          <input
                            value={draft.apiKey}
                            onChange={(event) =>
                              handleCredentialDraftChange(
                                credential.id,
                                "apiKey",
                                event.target.value
                              )
                            }
                            className="mt-2 w-full rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                          />
                        </label>
                        <label className="text-xs text-slate-600 dark:text-slate-300">
                          User
                          <input
                            value={draft.user}
                            onChange={(event) =>
                              handleCredentialDraftChange(
                                credential.id,
                                "user",
                                event.target.value
                              )
                            }
                            className="mt-2 w-full rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                          />
                        </label>
                      </div>
                      <button
                        type="button"
                        onClick={() => {
                          void handleCredentialSave(credential.id);
                        }}
                        disabled={isSaving}
                        className="mt-3 rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-emerald-300/60"
                      >
                        {isSaving ? "Saving..." : "Save"}
                      </button>
                    </div>
                  );
                })}
              </div>
            </>
          )}
        </section>

        <section className="mt-6 rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
          <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
            Session transfer
          </p>
          <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
            Move game sessions between accounts
          </h2>

          <div className="mt-4 flex flex-wrap gap-3">
            <input
              value={sessionQuery}
              onChange={(event) => setSessionQuery(event.target.value)}
              placeholder="Search by email, game, account user, or platform..."
              className="w-full max-w-md rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
            />
            <button
              type="button"
              onClick={handleSessionSearch}
              disabled={isSessionListLoading}
              className="rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-emerald-300/60"
            >
              {isSessionListLoading ? "Loading..." : "Search"}
            </button>
          </div>

          <div className="mt-4 flex flex-wrap items-end gap-3">
            <label className="text-xs text-slate-600 dark:text-slate-300">
              Target account
              <select
                value={targetCredentialId}
                onChange={(event) => setTargetCredentialId(event.target.value)}
                className="mt-2 block w-full min-w-[18rem] rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
              >
                <option value="">Select account...</option>
                {credentials.map((credential) => (
                  <option key={credential.id} value={credential.id}>
                    {credential.userEmail} ·{" "}
                    {formatProviderName(credential.platform)} ·{" "}
                    {credential.user ?? "Local"} (ID {credential.id})
                  </option>
                ))}
              </select>
            </label>
            <button
              type="button"
              onClick={toggleSelectAllVisibleSessions}
              disabled={movableSessions.length === 0}
              className="rounded-full border border-slate-300 dark:border-slate-700 px-3 py-2 text-xs font-semibold uppercase tracking-[0.25em] text-slate-700 dark:text-slate-200 transition hover:border-emerald-400/60 disabled:cursor-not-allowed disabled:opacity-60"
            >
              {allVisibleSessionsSelected ? "Clear visible" : "Select visible"}
            </button>
            <button
              type="button"
              onClick={handleMoveSessions}
              disabled={isMovingSessions || selectedSessionIds.length === 0}
              className="rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-emerald-300/60"
            >
              {isMovingSessions
                ? "Moving..."
                : `Move selected (${selectedSessionIds.length})`}
            </button>
          </div>

          {moveSessionsError ? (
            <p className="mt-3 text-xs text-rose-700 dark:text-rose-300">
              {moveSessionsError}
            </p>
          ) : null}
          {moveSessionsNotice ? (
            <p className="mt-3 text-xs text-emerald-700 dark:text-emerald-300">
              {moveSessionsNotice}
            </p>
          ) : null}

          {sessionTotal > 0 ? (
            <div className="mt-4 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-200/40 dark:bg-slate-950/40 px-3 py-2 text-xs">
              <p className="text-slate-600 dark:text-slate-300">
                Showing {sessionRangeStart}-{sessionRangeEnd} of {sessionTotal}
              </p>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => handleSessionPageChange(sessionPage - 1)}
                  disabled={sessionPage <= 1 || isSessionListLoading}
                  className="rounded-full border border-slate-300 dark:border-slate-700 px-3 py-1 font-semibold uppercase tracking-[0.2em] text-slate-700 dark:text-slate-200 transition hover:border-emerald-400/60 disabled:cursor-not-allowed disabled:opacity-60"
                >
                  Prev
                </button>
                <span className="font-semibold text-slate-700 dark:text-slate-200">
                  Page {sessionPage}/{sessionPageCount}
                </span>
                <button
                  type="button"
                  onClick={() => handleSessionPageChange(sessionPage + 1)}
                  disabled={
                    sessionPage >= sessionPageCount || isSessionListLoading
                  }
                  className="rounded-full border border-slate-300 dark:border-slate-700 px-3 py-1 font-semibold uppercase tracking-[0.2em] text-slate-700 dark:text-slate-200 transition hover:border-emerald-400/60 disabled:cursor-not-allowed disabled:opacity-60"
                >
                  Next
                </button>
              </div>
            </div>
          ) : null}

          <div className="mt-4 space-y-2">
            {isSessionListLoading ? (
              <p className="text-xs text-slate-500 dark:text-slate-400">
                Loading sessions...
              </p>
            ) : movableSessions.length === 0 ? (
              <p className="text-xs text-slate-500 dark:text-slate-400">
                No sessions found for this search.
              </p>
            ) : (
              movableSessions.map((session) => (
                <label
                  key={session.sessionId}
                  className="flex items-start gap-3 rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-200/40 dark:bg-slate-950/40 px-3 py-2"
                >
                  <input
                    type="checkbox"
                    checked={selectedSessionIds.includes(session.sessionId)}
                    onChange={() => toggleSessionSelection(session.sessionId)}
                    className="mt-1"
                  />
                  <div className="min-w-0 flex-1 text-xs">
                    <p className="truncate font-medium text-slate-900 dark:text-white">
                      {session.gameName}
                    </p>
                    <p className="mt-1 text-slate-500 dark:text-slate-400">
                      {session.userEmail} ·{" "}
                      {formatProviderName(session.platform)} ·{" "}
                      {session.accountUser ?? "Local"} · Session #
                      {session.sessionId}
                    </p>
                    <p className="mt-1 text-slate-500 dark:text-slate-400">
                      {new Date(session.startedAt).toLocaleString()} ·{" "}
                      {formatDuration(session.durationSeconds, "short")}
                    </p>
                  </div>
                  <span
                    className={`shrink-0 rounded-full px-2 py-1 text-xs font-semibold uppercase tracking-[0.2em] ${
                      session.live
                        ? "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300"
                        : "bg-slate-200 text-slate-600 dark:bg-slate-800 dark:text-slate-300"
                    }`}
                  >
                    {session.live ? "Live" : "Ended"}
                  </span>
                </label>
              ))
            )}
          </div>
        </section>

        <section className="mt-6 rounded-3xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/60 p-5 sm:p-6">
          <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
            Game details
          </p>
          <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
            Update title and image without next poll override
          </h2>
          <div className="mt-4 flex flex-wrap gap-3">
            <input
              value={gameQuery}
              onChange={(event) => setGameQuery(event.target.value)}
              placeholder="Search by game ID, title, or image URL..."
              aria-label="Search games by game id, title, or image URL"
              className="w-full max-w-sm rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
            />
            <button
              type="button"
              onClick={handleGameSearch}
              disabled={isGamesLoading}
              className="rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-emerald-300/60"
            >
              {isGamesLoading ? "Searching..." : "Search"}
            </button>
          </div>
          {gamesError ? (
            <p className="mt-4 text-xs text-rose-700 dark:text-rose-300">
              {gamesError}
            </p>
          ) : null}
          <div className="mt-5 space-y-3">
            {!hasGameSearch && !gamesError && !isGamesLoading ? (
              <p className="text-xs text-slate-500 dark:text-slate-400">
                Search by game ID, title, or image URL to load editable results.
              </p>
            ) : null}
            {hasGameSearch &&
            !gamesError &&
            !isGamesLoading &&
            games.length === 0 ? (
              <p className="text-xs text-slate-500 dark:text-slate-400">
                No games found for this query.
              </p>
            ) : null}
            {games.map((game) => {
              const draft = gameDrafts[game.id] ?? {
                title: game.title,
                lockTitle: game.titleLocked,
                imageUrl: game.imageUrl ?? "",
                lockImage: game.imageLocked,
              };
              const isSaving = savingGameIds.includes(game.id);
              return (
                <div
                  key={game.id}
                  className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/40 dark:bg-slate-950/40 p-4"
                >
                  <p className="text-sm font-semibold text-slate-900 dark:text-white">
                    {game.title} · {formatProviderName(game.platform)}
                  </p>
                  <p className="mt-1 text-xs text-slate-500 dark:text-slate-400">
                    Game ID: <span className="font-mono">{game.id}</span>
                  </p>
                  <div className="mt-3 grid gap-3 md:grid-cols-2">
                    <label className="text-xs text-slate-600 dark:text-slate-300">
                      Title
                      <input
                        value={draft.title}
                        onChange={(event) =>
                          handleGameDraftChange(
                            game.id,
                            "title",
                            event.target.value
                          )
                        }
                        className="mt-2 w-full rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                      />
                    </label>
                    <label className="text-xs text-slate-600 dark:text-slate-300">
                      Image URL
                      <input
                        value={draft.imageUrl}
                        onChange={(event) =>
                          handleGameDraftChange(
                            game.id,
                            "imageUrl",
                            event.target.value
                          )
                        }
                        className="mt-2 w-full rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                      />
                    </label>
                  </div>
                  <div className="mt-3 space-y-2">
                    <label className="flex w-full items-center gap-2 text-xs text-slate-600 dark:text-slate-300">
                      <input
                        type="checkbox"
                        checked={draft.lockTitle}
                        onChange={(event) =>
                          handleGameDraftChange(
                            game.id,
                            "lockTitle",
                            event.target.checked
                          )
                        }
                      />
                      Lock title against polling updates
                    </label>
                    <label className="flex w-full items-center gap-2 text-xs text-slate-600 dark:text-slate-300">
                      <input
                        type="checkbox"
                        checked={draft.lockImage}
                        onChange={(event) =>
                          handleGameDraftChange(
                            game.id,
                            "lockImage",
                            event.target.checked
                          )
                        }
                      />
                      Lock image against polling updates
                    </label>
                  </div>
                  <div className="mt-3">
                    <button
                      type="button"
                      onClick={() => {
                        void handleGameSave(game.id);
                      }}
                      disabled={isSaving}
                      className="rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-emerald-300/60"
                    >
                      {isSaving ? "Saving..." : "Save"}
                    </button>
                  </div>
                </div>
              );
            })}
          </div>
        </section>
      </div>
    </main>
  );
};
