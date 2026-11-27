import { describe, expect, it } from "vitest";
import { addItem, CART_STORAGE_KEY, countItems, MAX_QUANTITY, readCart, setQuantity, writeCart } from "./cart-store";

const khamrah = { variantId: "v1", slug: "lattafa-khamrah-edp" };

describe("cart store", () => {
  it("merges the same variant and caps the quantity", () => {
    let items = addItem([], khamrah);
    items = addItem(items, khamrah, 2);
    expect(items).toEqual([{ ...khamrah, quantity: 3 }]);
    expect(addItem(items, khamrah, 50)[0]!.quantity).toBe(MAX_QUANTITY);
    expect(countItems(addItem(items, { variantId: "v2", slug: "yara" }))).toBe(4);
  });

  it("removes a line at quantity 0", () => {
    expect(setQuantity([{ ...khamrah, quantity: 2 }], "v1", 0)).toEqual([]);
  });

  it("survives corrupted or tampered storage", () => {
    localStorage.setItem(CART_STORAGE_KEY, "{not json");
    expect(readCart(localStorage)).toEqual([]);
    localStorage.setItem(CART_STORAGE_KEY, JSON.stringify([{ variantId: 1 }, { variantId: "v1", slug: "x", quantity: 99 }, { variantId: "v2", slug: "y", quantity: -3 }]));
    expect(readCart(localStorage)).toEqual([{ variantId: "v1", slug: "x", quantity: MAX_QUANTITY }]);
  });

  it("stores only ids, slugs and quantities (never prices)", () => {
    writeCart(localStorage, [{ ...khamrah, quantity: 1 }]);
    expect(JSON.parse(localStorage.getItem(CART_STORAGE_KEY)!)).toEqual([{ variantId: "v1", slug: "lattafa-khamrah-edp", quantity: 1 }]);
  });
});
