import type { Elysia } from "elysia";

import type { PollingNotifier } from "../polling";
import {
  endPlayniteSessionsHandler,
  pingPlayniteSessionHandler,
  recordPlayniteSessionHandler,
  startPlayniteSessionHandler,
} from "./playnite.handlers";

export const registerPlayniteRoutes = (
  app: Elysia,
  notifier?: PollingNotifier
) =>
  app
    .post("/api/playnite/sessions", recordPlayniteSessionHandler)
    .post(
      "/api/playnite/sessions/start",
      startPlayniteSessionHandler(notifier)
    )
    .post(
      "/api/playnite/sessions/ping",
      pingPlayniteSessionHandler(notifier)
    )
    .post("/api/playnite/sessions/end", endPlayniteSessionsHandler(notifier));
