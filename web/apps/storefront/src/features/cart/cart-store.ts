/**
 * The cart lives in the browser (localStorage) and holds only what the customer chose: variant, quantity and the
 * product slug for the link back. Names, prices, delivery fee and totals always come from POST /api/cart/quote.
 */
export interface CartItem {
  variantId: string;
  slug: string;
  quantity: number;
}

export const CART_STORAGE_KEY = "sahara-cart-v1";
export const MAX_QUANTITY = 10;

export function readCart(storage: Storage | undefined): CartItem[] {
  try {
    const raw = storage?.getItem(CART_STORAGE_KEY);
    const parsed: unknown = raw ? JSON.parse(raw) : [];
    if (!Array.isArray(parsed)) {
      return [];
    }

    return parsed
      .filter((i): i is CartItem => typeof i === "object" && i !== null && typeof i.variantId === "string" && typeof i.slug === "string" && Number.isInteger(i.quantity))
      .map((i) => ({ variantId: i.variantId, slug: i.slug, quantity: clamp(i.quantity) }))
      .filter((i) => i.quantity > 0);
  } catch {
    return []; // corrupted or blocked storage: start with an empty cart
  }
}

export function writeCart(storage: Storage | undefined, items: CartItem[]) {
  try {
    storage?.setItem(CART_STORAGE_KEY, JSON.stringify(items));
  } catch {
    // private mode / quota: the cart still works for this visit
  }
}

const clamp = (quantity: number) => Math.max(0, Math.min(MAX_QUANTITY, Math.trunc(quantity)));

export function addItem(items: CartItem[], item: Omit<CartItem, "quantity">, quantity = 1): CartItem[] {
  const existing = items.find((i) => i.variantId === item.variantId);
  return existing
    ? items.map((i) => (i === existing ? { ...i, quantity: clamp(i.quantity + quantity) } : i))
    : [...items, { ...item, quantity: clamp(quantity) }];
}

export function setQuantity(items: CartItem[], variantId: string, quantity: number): CartItem[] {
  return items.map((i) => (i.variantId === variantId ? { ...i, quantity: clamp(quantity) } : i)).filter((i) => i.quantity > 0);
}

export const countItems = (items: CartItem[]) => items.reduce((sum, i) => sum + i.quantity, 0);
