import { storage } from "./storage";

const AccessTokenKey = "playdex.token";
const RefreshTokenKey = "playdex.refresh";
const EmailKey = "playdex.email";
/** Whether to show admin entry points; the API enforces the role on every admin call. */
const AdminKey = "playdex.admin";

export interface LoginRequest {
  email: string | null;
  password: string | null;
}

export interface LoginResponse {
  userId: number;
  email: string;
  isAdmin?: boolean;
  accessToken: string;
  refreshToken: string;
}

export interface RefreshResponse {
  accessToken: string;
  refreshToken: string;
}

export interface MeResponse {
  userId: number;
  email: string;
  isAdmin?: boolean;
}

export const authSession = {
  isLoggedIn(): boolean {
    return !!authSession.getAccessToken();
  },
  getAccessToken(): string | null {
    return storage.get(AccessTokenKey);
  },
  getRefreshToken(): string | null {
    return storage.get(RefreshTokenKey);
  },
  getEmail(): string | null {
    return storage.get(EmailKey);
  },
  isAdmin(): boolean {
    return storage.get(AdminKey) === "true";
  },
  storeLogin(response: LoginResponse): void {
    storage.set(AccessTokenKey, response.accessToken);
    storage.set(RefreshTokenKey, response.refreshToken);
    storage.set(EmailKey, response.email);
    storeAdminFlag(response.isAdmin);
  },
  storeProfile(response: MeResponse): void {
    storage.set(EmailKey, response.email);
    storeAdminFlag(response.isAdmin);
  },
  storeTokens(accessToken: string, refreshToken: string): void {
    storage.set(AccessTokenKey, accessToken);
    storage.set(RefreshTokenKey, refreshToken);
  },
  logout(): void {
    storage.remove(AccessTokenKey);
    storage.remove(RefreshTokenKey);
    storage.remove(EmailKey);
    storage.remove(AdminKey);
  },
};

function storeAdminFlag(isAdmin: boolean | undefined): void {
  if (isAdmin) storage.set(AdminKey, "true");
  else storage.remove(AdminKey);
}
