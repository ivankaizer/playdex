import { ok } from "../../app/http/responses";
import type { ApiResponse } from "../../app/http/types";

export const rootHandler = async (): Promise<ApiResponse<string>> => ok("Ok");
