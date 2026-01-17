import type postgres from "postgres";

import {
  endActiveSessionsForUser,
  loadGameById,
  loadNotificationTargetByCredential,
  loadUserByChatId,
} from "../../shared/db/queries";
import {
  fetchTelegramUpdates,
  sendTelegramMessage,
  sendTelegramImage,
} from "./client";
import {
  getErrorMessage,
  logEvent,
} from "../../shared/observability";
import { renderNotificationWebp } from "./notification-image";

import type { PollingNotifier } from "../polling/polling";
import { startManualSession } from "../manual/manual";

type Sql = ReturnType<typeof postgres>;

type TelegramBotConfig = {
  token: string;
  sql: Sql;
};

const FALLBACK_IMAGE_DATA_URL = `data:image/svg+xml;utf8,${encodeURIComponent(
  `<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="720"><rect width="100%" height="100%" fill="#0a0d12"/></svg>`
)}`;

const formatGameLabel = (game: { title: string; platform: string } | null) => {
  if (!game) {
    return "Unknown game";
  }
  return `${game.title} (${game.platform})`;
};

const formatDuration = (totalSeconds: number) => {
  const clamped = Math.max(0, Math.floor(totalSeconds));
  const hours = Math.floor(clamped / 3600);
  const minutes = Math.floor((clamped % 3600) / 60);
  const seconds = clamped % 60;

  if (hours > 0) {
    return `${hours}h ${minutes}m`;
  }
  if (minutes > 0) {
    return `${minutes}m ${seconds}s`;
  }
  return `${seconds}s`;
};

const buildSessionCaption = (
  kind: "started" | "ended",
  title: string,
  platform: string,
  duration?: string
) => {
  const headline = kind === "ended" ? "Session ended" : "Session started";
  const lines = [`${headline}: ${title} ${platform}`];
  if (duration) {
    lines.push(`Duration: ${duration}`);
  }
  return lines.join("\n");
};

const sendNotification = async (
  token: string,
  chatId: string,
  text: string
) => {
  await sendTelegramMessage(token, chatId, text);
};

const sendSessionImage = async ({
  token,
  chatId,
  kind,
  game,
  duration,
}: {
  token: string;
  chatId: string;
  kind: "started" | "ended";
  game: { title: string; platform: string; capsule_url: string | null } | null;
  duration?: string;
}) => {
  const title = game?.title ?? "Unknown game";
  const platform = game?.platform ?? "unknown";
  const caption = buildSessionCaption(kind, title, platform, duration);

  if (!game?.capsule_url) {
    await sendNotification(token, chatId, `${caption}.`);
    return;
  }

  const image = game.capsule_url;
  try {
    const buffer = await renderNotificationWebp({
      title,
      platform,
      image,
      kind,
      duration,
    });

    await sendTelegramImage(token, chatId, buffer, undefined, caption);
  } catch (error) {
    logEvent("error", "telegram.image.failed", {
      message: getErrorMessage(error),
      chatId,
      title,
      platform,
      kind,
    });
    await sendNotification(token, chatId, `${caption}.`);
  }
};

export const createTelegramNotifier = ({
  token,
  sql,
}: TelegramBotConfig): PollingNotifier => ({
  onPollFailure: async ({
    platformCredentialId,
    platform,
    message,
    backoffMs,
  }) => {
    const target = await loadNotificationTargetByCredential(
      platformCredentialId,
      sql
    );
    if (!target?.chat_id) {
      return;
    }

    const delaySeconds = Math.round(backoffMs / 1000);
    const text = [
      `Polling failed for ${platform}.`,
      message ? `Error: ${message}.` : null,
      `Retrying in ${delaySeconds}s.`,
    ]
      .filter(Boolean)
      .join(" ");

    await sendNotification(token, target.chat_id, text);
  },
  onSessionStarted: async ({ platformCredentialId, gameId }) => {
    const target = await loadNotificationTargetByCredential(
      platformCredentialId,
      sql
    );
    if (!target?.chat_id) {
      return;
    }

    const game = await loadGameById(gameId, sql);
    await sendSessionImage({
      token,
      chatId: target.chat_id,
      kind: "started",
      game,
    });
  },
  onSessionEnded: async ({ platformCredentialId, gameId, durationSeconds }) => {
    const target = await loadNotificationTargetByCredential(
      platformCredentialId,
      sql
    );
    if (!target?.chat_id) {
      return;
    }

    const game = await loadGameById(gameId, sql);
    const duration = formatDuration(durationSeconds);
    await sendSessionImage({
      token,
      chatId: target.chat_id,
      kind: "ended",
      game,
      duration,
    });
  },
});

const summarizeStoppedSessions = (
  sessions: { platform: string; title: string }[]
) => {
  if (sessions.length === 0) {
    return "No active sessions to stop.";
  }

  const details = sessions
    .map((session) => `${session.title} (${session.platform})`)
    .join(", ");
  return `Stopped ${sessions.length} session(s): ${details}.`;
};

const summarizeStartedSession = async (
  gameId: number,
  sql: Sql
): Promise<string> => {
  const game = await loadGameById(gameId, sql);
  return `Session started: ${formatGameLabel(game)}.`;
};

const handleStopCommand = async (token: string, sql: Sql, chatId: string) => {
  const user = await loadUserByChatId(chatId, sql);
  if (!user) {
    await sendNotification(
      token,
      chatId,
      "This chat is not linked to a Playdex user. Ask an admin to set chat_id on your user record."
    );
    return;
  }

  const endedAt = new Date();
  const sessions = await endActiveSessionsForUser(user.id, endedAt, sql);
  await sendNotification(token, chatId, summarizeStoppedSessions(sessions));
};

const handleStartCommand = async (
  token: string,
  sql: Sql,
  chatId: string,
  title: string
) => {
  const user = await loadUserByChatId(chatId, sql);
  if (!user) {
    await sendNotification(
      token,
      chatId,
      "This chat is not linked to a Playdex user. Ask an admin to set chat_id on your user record."
    );
    return;
  }

  try {
    const result = await startManualSession(user.id, title);
    const message = await summarizeStartedSession(result.gameId, sql);
    await sendNotification(token, chatId, message);
  } catch (error) {
    logEvent("error", "error.manual_entry", {
      error: error,
      chatId,
      title,
    });
    await sendNotification(
      token,
      chatId,
      "Unable to start a manual session right now."
    );
  }
};

const getCommand = (text: string) =>
  text.split(/\s+/).shift()?.toLowerCase() ?? "";

export const startTelegramBot = ({ token, sql }: TelegramBotConfig) => {
  let offset = 0;
  const pollDelayMs = 1000;

  const poll = async () => {
    try {
      const updates = await fetchTelegramUpdates(token, offset);

      for (const update of updates) {
        offset = Math.max(offset, update.update_id + 1);

        const message = update.message;
        if (!message?.text) {
          continue;
        }

        const text = message.text.trim();
        if (!text) {
          continue;
        }

        const command = getCommand(text);
        const chatId = String(message.chat.id);

        if (command.startsWith("/stop")) {
          await handleStopCommand(token, sql, chatId);
          continue;
        }

        if (command === "/start") {
          const title = text.slice(command.length).trim();
          if (!title) {
            await sendNotification(
              token,
              chatId,
              "Use /start <title> to begin a manual session, or /stop to end your current session."
            );
          } else {
            await handleStartCommand(token, sql, chatId, title);
          }
          continue;
        }

        if (command === "/help") {
          await sendNotification(
            token,
            chatId,
            "Use /start <title> to begin a manual session, or /stop to end your current session."
          );
        }
      }
    } catch (error) {
      logEvent("error", "telegram.poll.failed", {
        message: getErrorMessage(error),
      });
    } finally {
      setTimeout(() => {
        void poll();
      }, pollDelayMs);
    }
  };

  logEvent("info", "telegram.bot.started");
  void poll();
};
