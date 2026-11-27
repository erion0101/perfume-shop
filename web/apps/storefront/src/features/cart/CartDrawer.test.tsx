import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { AddToCartButton } from "@/components/product/AddToCartButton";
import { json, mockFetch, renderWithCart } from "@/test/utils";
import { CartButton, CartDrawer } from "./CartDrawer";

const line = (quantity: number) => ({
  variantId: "v1", quantity, sku: "LAT-KHA-30", brandName: "Lattafa", fragranceName: "Khamrah", variantType: "Mini", sizeValue: 30, sizeUnit: "Ml",
  imageUrl: null, unitPrice: 19.9, lineTotal: 19.9 * quantity, isAvailable: true, problem: null,
});

/** The API's quote: these numbers are what the drawer must show (it never computes them). */
function quoteFor(quantity: number) {
  const subtotal = Math.round(19.9 * quantity * 100) / 100;
  const free = subtotal >= 40;
  return {
    lines: [line(quantity)], currency: "EUR", countryCode: "XK", deliveryAvailable: true, subtotal, deliveryFee: free ? 0 : 2,
    total: subtotal + (free ? 0 : 2), vatAmount: 3.34, freeDeliveryFrom: 40, amountUntilFreeDelivery: free ? null : Math.round((40 - subtotal) * 100) / 100,
    canCheckout: true, problems: [],
  };
}

describe("cart drawer", () => {
  it("adds, opens, shows totals from the quote and re-quotes when the quantity changes (fast taps included)", async () => {
    const calls = mockFetch({ "POST /api/cart/quote": (_, body) => json(quoteFor((body as { items: { quantity: number }[] }).items[0]!.quantity)) });
    renderWithCart(
      <>
        <CartButton />
        <AddToCartButton variantId="v1" slug="lattafa-khamrah-edp" name="Lattafa Khamrah" />
        <CartDrawer />
      </>,
    );

    await userEvent.click(await screen.findByRole("button", { name: "Shto në shportë" }));
    const drawer = await screen.findByRole("dialog", { name: "Shporta" });
    expect(await within(drawer).findByText(/21,90/)).toBeInTheDocument();
    expect(within(drawer).getByText(/Edhe 20,10.€ dhe dërgesa është falas\./)).toBeInTheDocument();
    expect(calls.at(-1)!.body).toEqual({ items: [{ variantId: "v1", quantity: 1 }], countryCode: "XK" });

    await userEvent.click(within(drawer).getByRole("button", { name: "Një më shumë Lattafa Khamrah" }));
    await userEvent.click(within(drawer).getByRole("button", { name: "Një më shumë Lattafa Khamrah" }));
    expect(await within(drawer).findByText("Dërgesa jote është falas.")).toBeInTheDocument();
    expect(within(drawer).getAllByText(/59,70/).length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Shporta, 3 artikuj" })).toBeInTheDocument();
    expect(JSON.parse(localStorage.getItem("sahara-cart-v1")!)).toEqual([{ variantId: "v1", slug: "lattafa-khamrah-edp", quantity: 3 }]);
  });

  it("Escape closes the drawer and focus goes back to the opener", async () => {
    mockFetch({ "POST /api/cart/quote": () => json(quoteFor(1)) });
    localStorage.setItem("sahara-cart-v1", JSON.stringify([{ variantId: "v1", slug: "k", quantity: 1 }]));
    renderWithCart(
      <>
        <CartButton />
        <CartDrawer />
      </>,
    );

    const opener = await screen.findByRole("button", { name: "Shporta, 1 artikull" });
    await userEvent.click(opener);
    expect(await screen.findByRole("dialog", { name: "Shporta" })).toBeInTheDocument();
    await userEvent.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(opener).toHaveFocus();
  });

  it("shows the problem from the quote and a retry when quoting fails", async () => {
    let fail = true;
    mockFetch({ "POST /api/cart/quote": () => (fail ? json({}, 500) : json({ ...quoteFor(1), lines: [{ ...line(1), isAvailable: false, problem: "OutOfStock" }], canCheckout: false })) });
    localStorage.setItem("sahara-cart-v1", JSON.stringify([{ variantId: "v1", slug: "k", quantity: 1 }]));
    renderWithCart(
      <>
        <CartButton />
        <CartDrawer />
      </>,
    );

    await userEvent.click(await screen.findByRole("button", { name: /Shporta/ }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Çmimet nuk u ngarkuan.");
    fail = false;
    await userEvent.click(screen.getByRole("button", { name: "Provo përsëri" }));
    expect(await screen.findByText("S'ka më në stok")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Vazhdo te porosia" })).toHaveAttribute("aria-disabled", "true");
  });
});
