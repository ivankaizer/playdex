import { loadUserByEmail } from "../../shared/db/queries";

export const authRepository = {
  loadUserByEmail: (email: string) => loadUserByEmail(email),
};

