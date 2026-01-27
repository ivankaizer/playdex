import { requireAuth } from "../../app/http/auth";
import { ok } from "../../app/http/responses";
import type {
  PlayniteKeyResponseDto,
  ProvidersResponseDto,
  ApiResponse,
} from "../../app/http/types";
import { providersService } from "./providers.service";

export const listProvidersHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<ProvidersResponseDto>> => {
  const auth = requireAuth(ctx.request);
  const providers = await providersService.getProviders(auth.userId);
  return ok({ providers });
};

export const getPlayniteKeyHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<PlayniteKeyResponseDto>> => {
  const auth = requireAuth(ctx.request);
  const apiKey = await providersService.getOrCreatePlayniteKey(auth.userId);
  return ok({ apiKey });
};

