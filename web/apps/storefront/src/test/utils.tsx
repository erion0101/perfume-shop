import { render } from "@testing-library/react";
import type { ReactElement } from "react";
import { vi } from "vitest";
import { CartProvider } from "@/features/cart/CartProvider";

export const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

type Handler = (url: URL, body: unknown) => Response | Promise<Response>;

/** Replaces fetch with "METHOD /path" handlers; unknown calls fail loudly. Returns the calls made. */
export function mockFetch(handlers: Record<string, Handler>) {
  const calls: { method: string; url: URL; body: unknown }[] = [];
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(typeof input === "string" ? input : input instanceof URL ? input.href : input.url, "http://shop.test");
      const method = init?.method ?? "GET";
      const body = typeof init?.body === "string" ? JSON.parse(init.body) : undefined;
      calls.push({ method, url, body });
      const handler = handlers[`${method} ${url.pathname}`];
      if (!handler) {
        throw new Error(`Unexpected fetch: ${method} ${url.pathname}`);
      }

      return handler(url, body);
    }),
  );
  return calls;
}

export const renderWithCart = (ui: ReactElement) => render(<CartProvider>{ui}</CartProvider>);

export const money = (amount: number, currency = "EUR") => ({ amount, currency });

/** A listing card as the API sends it. */
export function cardFixture(overrides: Record<string, unknown> = {}) {
  return {
    id: "f1",
    slug: "lattafa-khamrah-edp",
    name: "Khamrah",
    brandName: "Lattafa",
    brandSlug: "lattafa",
    concentration: "EauDeParfum",
    gender: "Unisex",
    olfactiveFamily: "Amber",
    fromPrice: money(39.9),
    topAccords: [{ name: "sweet", percentage: 100 }],
    image: null,
    inStock: true,
    mood: "Sweet",
    defaultVariant: { id: "v1", type: "FullBottle", size: { value: 100, unit: "Ml" }, price: money(39.9), compareAtPrice: null, inStock: true },
    variantCount: 1,
    ...overrides,
  };
}
