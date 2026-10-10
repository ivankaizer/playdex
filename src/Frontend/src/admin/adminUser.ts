import { createContext, useContext } from "react";

/** Email of the signed-in administrator (verified against /api/me by RequireAdmin). */
export const AdminUserContext = createContext<string>("");

export function useAdminEmail(): string {
  return useContext(AdminUserContext);
}
