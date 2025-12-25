import type { Elysia } from "elysia";

import {
  recordPlayniteSession,
  type PlayniteGameProvider,
} from "../../application/playnite";
import type { PlayniteSessionResponseDto } from "./types";
import { fail, ok, withApiErrorLogging } from "./utils";

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

export const registerPlayniteRoutes = (app: Elysia) =>
  app.post(
    "/api/playnite/sessions",
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

      const gameTitle = payload?.gameTitle?.trim() ?? "";
      if (!gameTitle) {
        set.status = 400;
        return fail("gameTitle is required.");
      }

      const gameIdValue = payload?.gameId;
      const gameId =
        typeof gameIdValue === "number"
          ? String(gameIdValue)
          : typeof gameIdValue === "string"
            ? gameIdValue.trim()
            : "";

      if (!gameId) {
        set.status = 400;
        return fail("gameId is required.");
      }

      const gameProvider = payload?.gameProvider?.trim().toLowerCase() ?? "";
      if (!allowedProviders.has(gameProvider as PlayniteGameProvider)) {
        set.status = 400;
        return fail("gameProvider must be steam or unknown.");
      }

      const result = await recordPlayniteSession({
        apiKey: token,
        durationInSeconds,
        gameTitle,
        gameId,
        gameProvider: gameProvider as PlayniteGameProvider,
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
  );
