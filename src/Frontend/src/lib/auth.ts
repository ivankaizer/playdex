import { storage } from "./storage";

const AccessTokenKey = "playdex.token";
const RefreshTokenKey = "playdex.refresh";
const EmailKey = "playdex.email";

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
  storeLogin(response: LoginResponse): void {
    storage.set(AccessTokenKey, response.accessToken);
    storage.set(RefreshTokenKey, response.refreshToken);
    storage.set(EmailKey, response.email);
  },
  storeProfile(response: MeResponse): void {
    storage.set(EmailKey, response.email);
  },
  storeTokens(accessToken: string, refreshToken: string): void {
    storage.set(AccessTokenKey, accessToken);
    storage.set(RefreshTokenKey, refreshToken);
  },
  logout(): void {
    storage.remove(AccessTokenKey);
    storage.remove(RefreshTokenKey);
    storage.remove(EmailKey);
  },
};
