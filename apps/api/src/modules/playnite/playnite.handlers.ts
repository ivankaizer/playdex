import { ok } from "../../app/http/responses";
import type {
  ApiResponse,
  PlayniteSessionEndResponseDto,
  PlayniteSessionPingResponseDto,
} from "../../app/http/types";
import { AppError } from "../../common/errors/AppError";
import { getErrorMessage, logEvent } from "../../common/observability";
import type { PollingNotifier } from "../polling";
import { playniteService, type PlayniteGameProvider } from "./playnite.service";

type PlayniteSessionBody = {
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

const parseApiKey = (request: Request) => {
  const token = parseBearerToken(request.headers.get("authorization"));
  if (!token) {
    throw new AppError("Missing api key.", 401);
  }
  return token;
};

const allowedProviders = new Set<PlayniteGameProvider>(["steam", "unknown"]);

const parseGamePayload = (body: unknown) => {
  const payload = body as PlayniteSessionBody | null;
  const gameTitle = payload?.gameTitle?.trim() ?? "";
  if (!gameTitle) {
    throw new AppError("gameTitle is required.", 400);
  }

  const gameIdValue = payload?.gameId;
  const gameId =
    typeof gameIdValue === "number"
      ? String(gameIdValue)
      : typeof gameIdValue === "string"
        ? gameIdValue.trim()
        : "";

  if (!gameId) {
    throw new AppError("gameId is required.", 400);
  }

  const gameProvider = payload?.gameProvider?.trim().toLowerCase() ?? "";
  if (!allowedProviders.has(gameProvider as PlayniteGameProvider)) {
    throw new AppError("gameProvider must be steam or unknown.", 400);
  }

  return {
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

const notifyEndedSessions = async (
  notifier: PollingNotifier | undefined,
  payload: {
    platformCredentialId: number;
    gameId: number;
    startedAt: Date;
    endedAt: Date;
  }[]
) => {
  for (const session of payload) {
    const durationSeconds = Math.max(
      0,
      Math.round(
        (session.endedAt.getTime() - session.startedAt.getTime()) / 1000
      )
    );
    await safeNotify(
      notifier?.onSessionEnded,
      {
        platformCredentialId: session.platformCredentialId,
        gameId: session.gameId,
        startedAt: session.startedAt,
        endedAt: session.endedAt,
        durationSeconds,
      },
      "session.ended"
    );
  }
};

export const endPlayniteSessionsHandler =
  (notifier?: PollingNotifier) =>
  async (ctx: {
    request: Request;
    body?: unknown;
  }): Promise<ApiResponse<PlayniteSessionEndResponseDto>> => {
    const token = parseApiKey(ctx.request);
    const parsed = parseGamePayload(ctx.body);

    const result = await playniteService.endSessions({
      apiKey: token,
      gameTitle: parsed.gameTitle,
      gameId: parsed.gameId,
      gameProvider: parsed.gameProvider,
    });

    if (!result) {
      throw new AppError("Invalid api key.", 401);
    }

    await notifyEndedSessions(
      notifier,
      result.endedSessions.map((session) => ({
        platformCredentialId: result.platformCredentialId,
        gameId: session.gameId,
        startedAt: session.startedAt,
        endedAt: result.endedAt,
      }))
    );

    return ok({ endedSessionCount: result.endedSessions.length });
  };

export const pingPlayniteSessionHandler =
  (notifier?: PollingNotifier) =>
  async (ctx: {
    request: Request;
    body?: unknown;
  }): Promise<ApiResponse<PlayniteSessionPingResponseDto>> => {
    const token = parseApiKey(ctx.request);
    const parsed = parseGamePayload(ctx.body);

    const result = await playniteService.pingSession({
      apiKey: token,
      gameTitle: parsed.gameTitle,
      gameId: parsed.gameId,
      gameProvider: parsed.gameProvider,
    });

    if (!result) {
      throw new AppError("Invalid api key.", 401);
    }

    await notifyEndedSessions(
      notifier,
      result.endedSessions.map((session) => ({
        platformCredentialId: session.platformCredentialId,
        gameId: session.gameId,
        startedAt: session.startedAt,
        endedAt: session.endedAt,
      }))
    );

    if (result.sessionStarted) {
      await safeNotify(
        notifier?.onSessionStarted,
        {
          platformCredentialId: result.platformCredentialId,
          gameId: result.gameId,
          startedAt: result.startedAt,
        },
        "session.started"
      );
    }

    return ok({
      sessionId: result.sessionId,
      started: result.sessionStarted,
      endedSessionCount: result.endedSessions.length,
    });
  };
