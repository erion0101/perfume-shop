import type { Schema } from "@perfumeshop/api-client";

/** The signed-in staff member. The access token lives only in memory; the refresh token in an HttpOnly cookie. */
export interface Session {
  accessToken: string;
  expiresAt: number;
  userId: string;
  email: string;
  displayName: string | null;
  roles: string[];
  twoFactorEnabled: boolean;
  /** Admin without 2FA while it is required: admin pages answer 403 until it is enabled. */
  twoFactorSetupRequired: boolean;
}

export function toSession(response: Schema<"StaffSessionResponse">): Session {
  return {
    accessToken: response.accessToken,
    expiresAt: Date.parse(response.accessTokenExpiresAtUtc),
    userId: response.userId,
    email: response.email,
    displayName: response.displayName ?? null,
    roles: response.roles,
    twoFactorEnabled: response.twoFactorEnabled,
    twoFactorSetupRequired: response.twoFactorSetupRequired,
  };
}

type Listener = (session: Session | null) => void;

let current: Session | null = null;
const listeners = new Set<Listener>();

export const sessionStore = {
  get: () => current,
  set(session: Session | null) {
    current = session;
    listeners.forEach((listener) => listener(session));
  },
  subscribe(listener: Listener) {
    listeners.add(listener);
    return () => {
      listeners.delete(listener);
    };
  },
};

export const isAdmin = (session: Session | null) => session?.roles.includes("Admin") ?? false;
