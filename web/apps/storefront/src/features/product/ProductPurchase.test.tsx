import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { NotePyramid } from "@/components/NotePyramid";
import { money, renderWithCart } from "@/test/utils";
import { ProductPurchase } from "./ProductPurchase";

const variant = (id: string, size: number, type: string, price: number, inStock: boolean, compareAt?: number) => ({
  id, sku: id, ean: null, type, size: { value: size, unit: "Ml" }, price: money(price), compareAtPrice: compareAt ? money(compareAt) : null, isActive: true, images: [], inStock,
});

const fragrance = {
  id: "f1", brand: { id: "b1", name: "Lattafa", slug: "lattafa" }, name: "Khamrah", slug: "lattafa-khamrah-edp", concentration: "EauDeParfum", gender: "Unisex",
  olfactiveFamily: "Amber", launchYear: 2022, perfumers: [], description: null, notes: { top: [], heart: [], base: [] }, accords: [], longevity: null, sillage: null,
  seasons: [], occasions: [], similarTo: [], inStock: true, mood: "Sweet", updatedAtUtc: "2026-11-27T10:00:00Z",
  variants: [variant("v30", 30, "Mini", 19.9, true), variant("v100", 100, "FullBottle", 39.9, false, 44.9)],
};

describe("ProductPurchase", () => {
  it("starts on a variant in stock and switching shows its price, saving and stock", async () => {
    renderWithCart(<ProductPurchase fragrance={fragrance} />);

    expect(screen.getByRole("radio", { name: "30 ml · Mini" })).toBeChecked();
    expect(screen.getAllByText(/19,90/).length).toBeGreaterThan(0);
    expect(screen.getAllByRole("button", { name: "Shto në shportë" })[0]).toBeEnabled();

    await userEvent.click(screen.getByRole("radio", { name: /100 ml/ }));
    expect(screen.getByText(/Kursen 5,00/)).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("S'ka në stok");
    expect(screen.getAllByRole("button", { name: "Shto në shportë" })[0]).toBeDisabled();
  });
});

describe("NotePyramid", () => {
  it("shows the layers in Albanian and leaves out empty ones", () => {
    renderWithCart(<NotePyramid notes={{ top: [{ name: "Cinnamon" }, { name: "Bergamot" }], heart: [], base: [{ name: "Vanilla" }, { name: "Agarwood" }] }} />);

    expect(screen.getByText("Në fillim")).toBeInTheDocument();
    expect(screen.getByText("Kanellë, bergamot")).toBeInTheDocument();
    expect(screen.queryByText("Zemra")).not.toBeInTheDocument();
    expect(screen.getByText("Vanilje, oud")).toBeInTheDocument();
  });
});
