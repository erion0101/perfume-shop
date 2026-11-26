import { LoadingState } from "@perfumeshop/ui";
import type { ReactNode } from "react";
import { Navigate, useLocation } from "react-router";
import { t } from "@/i18n";
import { useAuth } from "@/lib/auth";

/** Signed-in staff only; others go to /login and come back afterwards. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { session, ready } = useAuth();
  const location = useLocation();
  if (!ready) {
    return <LoadingState className="min-h-dvh" />;
  }

  return session ? children : <Navigate to="/login" replace state={{ from: `${location.pathname}${location.search}` }} />;
}

/** Admin-only pages (Catalog). The API enforces it too; this only avoids a page full of 403s. */
export function RequireAdmin({ children }: { children: ReactNode }) {
  const { isAdmin } = useAuth();
  return isAdmin ? children : <p className="rounded-md border border-border p-4 text-sm">{t.auth.forbidden}</p>;
}
