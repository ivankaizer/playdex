import type { ApiResponse } from "./types";

export const ok = <T>(data: T): ApiResponse<T> => ({ data, error: null });

export const fail = (message: string): ApiResponse<never> => ({
  data: null,
  error: { error: message },
});
