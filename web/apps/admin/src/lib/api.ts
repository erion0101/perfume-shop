import { createApiClient } from "@perfumeshop/api-client";
import { ApiError } from "./problem";
import { sessionStore, toSession, type Session } from "./session";

/** Empty = same origin (Vite proxies /api and /media to the API in development). */
export const apiBaseUrl: string = import.meta.env.VITE_API_URL ?? "";

/** Required by the session endpoints: a cross-site form cannot send it (CSRF protection). */
export const clientHeaders = { "X-PerfumeShop-Client": "admin" } as const;

const sessionPath = "/api/auth/staff/session";

let refreshing: Promise<Session | null> | null = null;

/** New access token from the HttpOnly refresh cookie. Concurrent callers share one request (rotation!). */
export function refreshSession(): Promise<Session | null> {
  refreshing ??= fetch(`${apiBaseUrl}${sessionPath}/refresh`, { method: "POST", credentials: "include", headers: clientHeaders })
    .then(async (response) => (response.ok ? toSession(await response.json()) : null))
    .catch(() => null)
    .then((session) => {
      sessionStore.set(session);
      return session;
    })
    .finally(() => {
      refreshing = null;
    });
  return refreshing;
}

/** Adds the bearer token; on 401 refreshes once and repeats the request; signs out when the refresh fails. */
export async function authFetch(input: Request): Promise<Response> {
  const retry = input.clone();
  const token = sessionStore.get()?.accessToken;
  if (token) {
    input.headers.set("Authorization", `Bearer ${token}`);
  }

  let response: Response;
  try {
    response = await fetch(input);
  } catch {
    throw ApiError.network();
  }

  if (response.status !== 401 || input.url.includes(sessionPath)) {
    return response;
  }

  const session = await refreshSession();
  if (!session) {
    return response; // sessionStore is now null → the app shows the login page
  }

  retry.headers.set("Authorization", `Bearer ${session.accessToken}`);
  try {
    return await fetch(retry);
  } catch {
    throw ApiError.network();
  }
}

export const api = createApiClient({ baseUrl: apiBaseUrl, fetch: authFetch, headers: clientHeaders, credentials: "include" });

/** Data of a successful call, or an {@link ApiError} with the ProblemDetails. */
export async function unwrap<T>(call: Promise<{ data?: T; error?: unknown; response: Response }>): Promise<T> {
  const { data, error, response } = await call;
  if (!response.ok) {
    throw new ApiError(response.status, (error as ConstructorParameters<typeof ApiError>[1]) ?? null);
  }

  return data as T;
}
