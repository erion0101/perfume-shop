import { useQueryClient } from "@tanstack/react-query";
import { createContext, useContext, useEffect, useState, useSyncExternalStore, type ReactNode } from "react";
import { api, apiBaseUrl, clientHeaders, refreshSession, unwrap } from "./api";
import { isAdmin, sessionStore, toSession, type Session } from "./session";

interface AuthContextValue {
  session: Session | null;
  /** False until the first silent refresh (page reload) has finished. */
  ready: boolean;
  isAdmin: boolean;
  signIn: (email: string, password: string) => Promise<void>;
  signOut: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const session = useSyncExternalStore(sessionStore.subscribe, sessionStore.get);
  const [ready, setReady] = useState(() => sessionStore.get() !== null);

  // After a reload the access token is gone; the HttpOnly cookie may still give a new one.
  useEffect(() => {
    if (!ready) {
      void refreshSession().finally(() => setReady(true));
    }
  }, [ready]);

  // Refresh a minute before the access token expires, so long warehouse sessions are not interrupted.
  useEffect(() => {
    if (!session) {
      return;
    }

    const timer = window.setTimeout(() => void refreshSession(), Math.max(10_000, session.expiresAt - Date.now() - 60_000));
    return () => window.clearTimeout(timer);
  }, [session]);

  const value: AuthContextValue = {
    session,
    ready,
    isAdmin: isAdmin(session),
    async signIn(email, password) {
      const response = await unwrap(api.POST("/api/auth/staff/session", { body: { email, password } }));
      queryClient.clear();
      sessionStore.set(toSession(response));
    },
    async signOut() {
      try {
        await fetch(`${apiBaseUrl}/api/auth/staff/session`, { method: "DELETE", credentials: "include", headers: clientHeaders });
      } finally {
        sessionStore.set(null);
        queryClient.clear();
      }
    },
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used inside <AuthProvider>.");
  }

  return context;
}
