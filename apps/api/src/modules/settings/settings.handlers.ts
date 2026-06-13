import type { AuthenticatedContext } from "../../app/http/auth";
import { ok } from "../../app/http/responses";
import type {
  ApiResponse,
  UserSettingsResponseDto,
} from "../../app/http/types";
import { AppError } from "../../common/errors/AppError";
import { settingsService } from "./settings.service";

type SettingsUpdateBody = {
  ignoreShortSessionsMinutes?: number;
};

const parseNonNegativeInteger = (value: unknown, field: string) => {
  const numeric =
    typeof value === "number" ? value : value ? Number(value) : Number.NaN;
  if (!Number.isInteger(numeric) || numeric < 0) {
    throw new AppError(`${field} must be a non-negative integer.`, 400);
  }
  return numeric;
};

export const getUserSettingsHandler = async (
  ctx: AuthenticatedContext
): Promise<ApiResponse<UserSettingsResponseDto>> => {
  const { auth } = ctx;
  const settings = await settingsService.getUserSettings(auth.userId);
  return ok(settings);
};

export const updateUserSettingsHandler = async (
  ctx: AuthenticatedContext & { body?: unknown }
): Promise<ApiResponse<UserSettingsResponseDto>> => {
  const { auth } = ctx;
  const payload = ctx.body as SettingsUpdateBody | null;
  const minutes = parseNonNegativeInteger(
    payload?.ignoreShortSessionsMinutes,
    "ignoreShortSessionsMinutes"
  );
  const settings = await settingsService.updateUserSettings(
    auth.userId,
    minutes
  );
  return ok(settings);
};
