import type { Elysia } from "elysia";

import { getOrCreatePlayniteKey, getProviders } from "../../application/providers";
import type { PlayniteKeyResponseDto, ProvidersResponseDto } from "./types";
import { ok, requireAuth, withApiErrorLogging } from "./utils";

export const registerProviderRoutes = (app: Elysia) =>
  app
    .get(
      "/providers",
      withApiErrorLogging(async ({ request, set }) => {
        const auth = requireAuth(request);
        if (!auth.ok) {
          set.status = auth.status;
          return auth.response;
        }

        const providers = await getProviders(auth.userId);
        const response: ProvidersResponseDto = { providers };
        return ok(response);
      })
    )
    .post(
      "/playnite/key",
      withApiErrorLogging(async ({ request, set }) => {
        const auth = requireAuth(request);
        if (!auth.ok) {
          set.status = auth.status;
          return auth.response;
        }

        const apiKey = await getOrCreatePlayniteKey(auth.userId);
        const response: PlayniteKeyResponseDto = { apiKey };
        return ok(response);
      })
    );
