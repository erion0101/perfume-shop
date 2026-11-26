import { render } from "@testing-library/react";
import type { ReactElement } from "react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { vi } from "vitest";
import { AppProviders } from "@/App";
import { createQueryClient } from "@/lib/query-client";
import { sessionStore, type Session } from "@/lib/session";

export const adminSession: Session = {
  accessToken: "access-1",
  expiresAt: Date.now() + 15 * 60_000,
  userId: "00000000-0000-0000-0000-000000000001",
  email: "admin@perfumeshop.test",
  displayName: "Admin",
  roles: ["Admin"],
  twoFactorEnabled: true,
  twoFactorSetupRequired: false,
};

type Handler = (request: Request, body: unknown) => Response | Promise<Response>;

export const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

export const problem = (status: number, code?: string, errors?: Record<string, string[]>) =>
  new Response(JSON.stringify({ status, title: code, code, errors }), { status, headers: { "Content-Type": "application/problem+json" } });

/**
 * Replaces fetch with "METHOD /path" handlers (query string ignored). Unknown calls fail the test loudly.
 * Returns the calls made, for assertions on what was sent.
 */
export function mockApi(handlers: Record<string, Handler>) {
  const calls: { method: string; path: string; body: unknown; headers: Headers }[] = [];
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(input, init);
    const url = new URL(request.url);
    const text = request.method === "GET" || request.method === "HEAD" ? "" : await request.clone().text();
    let body: unknown = text;
    try {
      body = text ? JSON.parse(text) : undefined;
    } catch {
      // multipart or plain text
    }

    calls.push({ method: request.method, path: url.pathname, body, headers: request.headers });
    const key = `${request.method} ${url.pathname}`;
    const handler = handlers[key] ?? Object.entries(handlers).find(([pattern]) => new RegExp(`^${pattern.replace(/\{[^}]+\}/g, "[^/]+")}$`).test(key))?.[1];
    if (!handler) {
      throw new Error(`Unexpected API call: ${key}`);
    }

    return handler(request, body);
  });
  vi.stubGlobal("fetch", fetchMock);
  return calls;
}

/** Renders a page inside the real providers and a memory router. */
export function renderPage(element: ReactElement, { path = "/", route = "/", session = adminSession as Session | null } = {}) {
  sessionStore.set(session);
  const router = createMemoryRouter(
    [
      { path, element },
      { path: "*", element: <p>navigated</p> },
    ],
    { initialEntries: [route] },
  );
  const result = render(
    <AppProviders queryClient={createQueryClient()}>
      <RouterProvider router={router} />
    </AppProviders>,
  );
  return { ...result, router };
}
