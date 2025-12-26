import type { Elysia } from "elysia";

import {
  endPlayniteSessions,
  recordPlayniteSession,
  startPlayniteSession,
  type PlayniteGameProvider,
} from "../../application/playnite";
import type { PollingNotifier } from "../../application/polling";
import type {
  PlayniteSessionEndResponseDto,
  PlayniteSessionResponseDto,
  PlayniteSessionStartResponseDto,
} from "./types";
import { fail, ok, withApiErrorLogging } from "./utils";
import { getErrorMessage, logEvent } from "../../shared/observability";

type PlayniteSessionBody = {
  durationInSeconds?: number;
  gameTitle: string;
  gameId?: string | number;
  gameProvider: string;
};

const parseBearerToken = (header: string | null) => {
  if (!header?.startsWith("Bearer ")) {
    return "";
  }
  return header.slice("Bearer ".length).trim();
};

const allowedProviders = new Set<PlayniteGameProvider>(["steam", "unknown"]);

type PlayniteGamePayload =
  | {
      ok: true;
      gameTitle: string;
      gameId: string;
      gameProvider: PlayniteGameProvider;
    }
  | { ok: false; message: string };

const parseGamePayload = (body: unknown): PlayniteGamePayload => {
  const payload = body as PlayniteSessionBody | null;
  const gameTitle = payload?.gameTitle?.trim() ?? "";
  if (!gameTitle) {
    return { ok: false, message: "gameTitle is required." };
  }

  const gameIdValue = payload?.gameId;
  const gameId =
    typeof gameIdValue === "number"
      ? String(gameIdValue)
      : typeof gameIdValue === "string"
        ? gameIdValue.trim()
        : "";

  if (!gameId) {
    return { ok: false, message: "gameId is required." };
  }

  const gameProvider = payload?.gameProvider?.trim().toLowerCase() ?? "";
  if (!allowedProviders.has(gameProvider as PlayniteGameProvider)) {
    return { ok: false, message: "gameProvider must be steam or unknown." };
  }

  return {
    ok: true,
    gameTitle,
    gameId,
    gameProvider: gameProvider as PlayniteGameProvider,
  };
};

const safeNotify = async <T>(
  handler: ((payload: T) => Promise<void>) | undefined,
  payload: T,
  eventName: string
) => {
  if (!handler) {
    return;
  }

  try {
    await handler(payload);
  } catch (error) {
    logEvent("error", "playnite.notification.failed", {
      event: eventName,
      message: getErrorMessage(error),
    });
  }
};

export const registerPlayniteRoutes = (
  app: Elysia,
  notifier?: PollingNotifier
) =>
  app.post(
    "/playnite/sessions",
    withApiErrorLogging(async ({ body, request, set }) => {
      const token = parseBearerToken(request.headers.get("authorization"));
      if (!token) {
        set.status = 401;
        return fail("Missing api key.");
      }

      const payload = body as PlayniteSessionBody | null;
      const durationRaw = payload?.durationInSeconds;
      const durationInSeconds =
        typeof durationRaw === "number" ? durationRaw : Number(durationRaw);

      if (!Number.isFinite(durationInSeconds) || durationInSeconds <= 0) {
        set.status = 400;
        return fail("durationInSeconds must be a positive number.");
      }

      const parsed = parseGamePayload(body);
      if (!parsed.ok) {
        set.status = 400;
        return fail(parsed.message);
      }

      const result = await recordPlayniteSession({
        apiKey: token,
        durationInSeconds,
        gameTitle: parsed.gameTitle,
        gameId: parsed.gameId,
        gameProvider: parsed.gameProvider,
      });

      if (!result) {
        set.status = 401;
        return fail("Invalid api key.");
      }

      const response: PlayniteSessionResponseDto = {
        sessionId: result.sessionId,
      };
      return ok(response);
    })
  )
    .post(
      "/playnite/sessions/start",
      withApiErrorLogging(async ({ body, request, set }) => {
        const token = parseBearerToken(request.headers.get("authorization"));
        if (!token) {
          set.status = 401;
          return fail("Missing api key.");
        }

        const parsed = parseGamePayload(body);
        if (!parsed.ok) {
          set.status = 400;
          return fail(parsed.message);
        }

        const result = await startPlayniteSession({
          apiKey: token,
          gameTitle: parsed.gameTitle,
          gameId: parsed.gameId,
          gameProvider: parsed.gameProvider,
        });

        if (!result) {
          set.status = 401;
          return fail("Invalid api key.");
        }

        for (const session of result.endedSessions) {
          await safeNotify(
            notifier?.onSessionEnded,
            {
              platformCredentialId: result.platformCredentialId,
              gameId: session.gameId,
              endedAt: result.startedAt,
            },
            "session.ended"
          );
        }

        await safeNotify(
          notifier?.onSessionStarted,
          {
            platformCredentialId: result.platformCredentialId,
            gameId: result.gameId,
            startedAt: result.startedAt,
          },
          "session.started"
        );

        const response: PlayniteSessionStartResponseDto = {
          sessionId: result.sessionId,
        };
        return ok(response);
      })
    )
    .post(
      "/playnite/sessions/end",
      withApiErrorLogging(async ({ request, set }) => {
        const token = parseBearerToken(request.headers.get("authorization"));
        if (!token) {
          set.status = 401;
          return fail("Missing api key.");
        }

        const result = await endPlayniteSessions({ apiKey: token });
        if (!result) {
          set.status = 401;
          return fail("Invalid api key.");
        }

        for (const session of result.endedSessions) {
          await safeNotify(
            notifier?.onSessionEnded,
            {
              platformCredentialId: result.platformCredentialId,
              gameId: session.gameId,
              endedAt: result.endedAt,
            },
            "session.ended"
          );
        }

        const response: PlayniteSessionEndResponseDto = {
          endedSessionCount: result.endedSessions.length,
        };
        return ok(response);
      })
    );
