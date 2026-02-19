import type { Elysia } from "elysia";

import { rootHandler } from "./root.handlers";

export const registerRootRoutes = (app: Elysia) => app.get("/api", rootHandler);
