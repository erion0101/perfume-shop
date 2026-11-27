"use client";

import type { Schema } from "@perfumeshop/api-client";
import { createContext, useCallback, useContext, useEffect, useMemo, useState, useSyncExternalStore, type ReactNode } from "react";
import { addItem, CART_STORAGE_KEY, countItems, readCart, setQuantity as setItemQuantity, writeCart, type CartItem } from "./cart-store";

export type CartQuote = Schema<"CartQuoteDto">;

export type QuoteState = { status: "idle" | "loading" | "ready" | "error"; quote: CartQuote | null };

interface CartContextValue {
  items: CartItem[];
  count: number;
  /** False on the server and during hydration (avoids a "0" flash and mismatches). */
  loaded: boolean;
  add: (item: Omit<CartItem, "quantity">, quantity?: number) => void;
  setQuantity: (variantId: string, quantity: number) => void;
  remove: (variantId: string) => void;
  clear: () => void;
  isOpen: boolean;
  open: () => void;
  close: () => void;
  quote: QuoteState;
  requote: () => void;
}

const CartContext = createContext<CartContextValue | null>(null);

// ---- localStorage as an external store (shared by tabs through the "storage" event) ----
const EMPTY: CartItem[] = [];
const listeners = new Set<() => void>();
let cache: { raw: string | null; items: CartItem[] } = { raw: null, items: EMPTY };

function snapshot(): CartItem[] {
  let raw: string | null = null;
  try {
    raw = window.localStorage.getItem(CART_STORAGE_KEY);
  } catch {
    // blocked storage
  }

  if (raw !== cache.raw) {
    cache = { raw, items: raw ? readCart(window.localStorage) : EMPTY };
  }

  return cache.items;
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  const onStorage = (e: StorageEvent) => (e.key === null || e.key === CART_STORAGE_KEY) && listener();
  window.addEventListener("storage", onStorage);
  return () => {
    listeners.delete(listener);
    window.removeEventListener("storage", onStorage);
  };
}

function save(items: CartItem[]) {
  writeCart(window.localStorage, items);
  listeners.forEach((l) => l());
}

const noopSubscribe = () => () => {};

/** POST /api/cart/quote (same-origin rewrite to the API). */
export async function fetchQuote(items: CartItem[], signal?: AbortSignal): Promise<CartQuote> {
  const response = await fetch("/api/cart/quote", {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify({ items: items.map((i) => ({ variantId: i.variantId, quantity: i.quantity })), countryCode: "XK" }),
    signal,
  });
  if (!response.ok) {
    throw new Error(`Quote failed: ${response.status}`);
  }

  return (await response.json()) as CartQuote;
}

/**
 * Re-quotes whenever `items` change while `enabled` (debounced 200 ms, older answers dropped).
 * Totals shown anywhere come from here: never computed in the browser.
 */
export function useQuote(items: CartItem[], enabled: boolean): [QuoteState, () => void] {
  const [result, setResult] = useState<{ key: string; state: QuoteState }>({ key: "", state: { status: "idle", quote: null } });
  const [version, setVersion] = useState(0);
  const key = `${version}:${JSON.stringify(items)}`;
  const active = enabled && items.length > 0;

  useEffect(() => {
    if (!active) {
      return;
    }

    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      fetchQuote(items, controller.signal).then(
        (quote) => setResult({ key, state: { status: "ready", quote } }),
        (error: unknown) => {
          if (!controller.signal.aborted) {
            console.error(error);
            setResult((r) => ({ key, state: { status: "error", quote: r.state.quote } }));
          }
        },
      );
    }, 200);
    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
    // `key` covers items and version.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, active]);

  const state: QuoteState = !enabled
    ? result.state
    : items.length === 0
      ? { status: "ready", quote: null }
      : result.key === key
        ? result.state
        : { status: "loading", quote: result.state.quote }; // keep showing the last totals while updating
  return [state, () => setVersion((v) => v + 1)];
}

export function CartProvider({ children }: { children: ReactNode }) {
  const items = useSyncExternalStore(subscribe, snapshot, () => EMPTY);
  const loaded = useSyncExternalStore(noopSubscribe, () => true, () => false);
  const [isOpen, setIsOpen] = useState(false);
  // Re-quoted on every open: prices and stock may have changed since the items were added.
  const [quote, requote] = useQuote(items, isOpen && loaded);

  const update = useCallback((change: (current: CartItem[]) => CartItem[]) => save(change(snapshot())), []);

  const value = useMemo<CartContextValue>(
    () => ({
      items,
      count: countItems(items),
      loaded,
      add: (item, quantity = 1) => update((c) => addItem(c, item, quantity)),
      setQuantity: (variantId, quantity) => update((c) => setItemQuantity(c, variantId, quantity)),
      remove: (variantId) => update((c) => setItemQuantity(c, variantId, 0)),
      clear: () => update(() => []),
      isOpen,
      open: () => setIsOpen(true),
      close: () => setIsOpen(false),
      quote,
      requote,
    }),
    [items, loaded, isOpen, quote, requote, update],
  );

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>;
}

export function useCart(): CartContextValue {
  const value = useContext(CartContext);
  if (!value) {
    throw new Error("useCart must be used inside <CartProvider>");
  }

  return value;
}
