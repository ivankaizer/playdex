import { requireAuth } from "../../app/http/auth";
import { ok } from "../../app/http/responses";
import { AppError } from "../../common/errors/AppError";
import type {
  ProviderUpdateResponseDto,
  PlayniteKeyResponseDto,
  ProvidersResponseDto,
  ApiResponse,
} from "../../app/http/types";
import { providersService } from "./providers.service";

const parseProviderId = (value: unknown) => {
  const numeric =
    typeof value === "number" ? value : value ? Number(value) : Number.NaN;
  if (!Number.isInteger(numeric) || numeric <= 0) {
    throw new AppError("providerId must be a positive integer.", 400);
  }
  return numeric;
};

const parseProviderStatus = (value: unknown) => {
  if (value !== "active" && value !== "paused") {
    throw new AppError("status must be active or paused.", 400);
  }
  return value;
};

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

export const updateProviderStatusHandler = async (ctx: {
  request: Request;
  body?: unknown;
}): Promise<ApiResponse<ProviderUpdateResponseDto>> => {
  const auth = requireAuth(ctx.request);
  const payload = ctx.body as
    | { providerId?: number | string; status?: unknown }
    | null;

  const providerId = parseProviderId(payload?.providerId ?? null);
  const status = parseProviderStatus(payload?.status ?? null);
  const provider = await providersService.updateProviderStatus(
    auth.userId,
    providerId,
    status
  );

  return ok({ provider });
};
