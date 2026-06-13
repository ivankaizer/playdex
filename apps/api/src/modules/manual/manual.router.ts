import { Elysia } from "elysia";

import { withAuth } from "../../app/http/auth";
import {
  createManualEntryHandler,
  listManualInputsHandler,
  mapManualInputHandler,
  searchManualMappingsHandler,
  startManualSessionHandler,
  stopManualSessionHandler,
} from "./manual.handlers";

export const registerManualRoutes = (app: Elysia) =>
  app.use(
    withAuth(new Elysia())
      .get("/api/manual/inputs", listManualInputsHandler)
      .post("/api/manual/sessions/start", startManualSessionHandler)
      .post("/api/manual/sessions/stop", stopManualSessionHandler)
      .get("/api/manual/search", searchManualMappingsHandler)
      .post("/api/manual/entries", createManualEntryHandler)
      .post("/api/manual/inputs/:inputId/map", mapManualInputHandler)
  );
