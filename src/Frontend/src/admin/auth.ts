import { ApiError, apiRequest } from "@/lib/api";
import { authSession, type LoginResponse, type MeResponse } from "@/lib/auth";

export const NotAdministratorMessage = "This account does not have administrator access.";

export type AdminLoginResult = { ok: true; email: string } | { ok: false; error: string };

/**
 * Signs in through the shared /api/login endpoint and keeps the session only for administrators.
 * Tokens are shared with the player UI, so a non-admin attempt leaves the browser signed out.
 */
export async function adminLogin(email: string, password: string): Promise<AdminLoginResult> {
  if (!email.trim() || !password) return { ok: false, error: "Email and password are required." };
  let response: LoginResponse;
  try {
    response = await apiRequest<LoginResponse>("login", {
      method: "POST",
      body: { email: email.trim(), password },
      authenticated: false,
    });
  } catch (error) {
    authSession.logout();
    if (error instanceof ApiError && error.status === 401) return { ok: false, error: "Invalid email or password." };
    return { ok: false, error: error instanceof Error ? error.message : "Sign in failed." };
  }

  if (response.isAdmin !== true) {
    authSession.logout();
    return { ok: false, error: NotAdministratorMessage };
  }

  authSession.storeLogin(response);
  return { ok: true, email: response.email };
}

export type AdminCheck = { status: "admin"; email: string } | { status: "anonymous" } | { status: "forbidden" };

/** Re-checks the stored session against /api/me (the API re-checks the role on every admin call too). */
export async function checkAdminSession(signal?: AbortSignal): Promise<AdminCheck> {
  if (!authSession.isLoggedIn()) return { status: "anonymous" };
  try {
    const me = await apiRequest<MeResponse>("me", { signal });
    return me.isAdmin === true ? { status: "admin", email: me.email } : { status: "forbidden" };
  } catch (error) {
    if (error instanceof ApiError && (error.status === 401 || error.status === 404)) {
      authSession.logout();
      return { status: "anonymous" };
    }
    throw error;
  }
}

export function adminLogout(): void {
  authSession.logout();
}
