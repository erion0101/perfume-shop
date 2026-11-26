import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { json, mockApi, problem, renderPage } from "@/test/utils";
import { BrandsPage } from "./BrandsPage";

const lattafa = { id: "b1", name: "Lattafa", slug: "lattafa", countryCode: "AE", logoUrl: null, description: null, fragranceCount: 3 };

describe("BrandsPage", () => {
  it("creates a brand and sends clean values", async () => {
    let brands = [lattafa];
    const calls = mockApi({
      "GET /api/admin/catalog/brands": () => json(brands),
      "POST /api/admin/catalog/brands": (_, body) => {
        brands = [...brands, { ...lattafa, id: "b2", name: "Armaf", slug: "armaf", fragranceCount: 0 }];
        return json(body, 201);
      },
    });
    renderPage(<BrandsPage />);

    expect((await screen.findAllByText("Lattafa")).length).toBeGreaterThan(0);
    await userEvent.click(screen.getByRole("button", { name: /Markë e re/ }));
    const dialog = await screen.findByRole("dialog");
    await userEvent.type(within(dialog).getByLabelText(/^Emri/), "Armaf");
    await userEvent.type(within(dialog).getByLabelText(/Shteti/), "ae");
    await userEvent.click(within(dialog).getByRole("button", { name: "Ruaj" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(calls.find((c) => c.method === "POST")!.body).toEqual({ name: "Armaf", slug: null, countryCode: "AE", logoUrl: null, description: null });
    expect((await screen.findAllByText("Armaf")).length).toBeGreaterThan(0);
  });

  it("validates before sending", async () => {
    const calls = mockApi({ "GET /api/admin/catalog/brands": () => json([]) });
    renderPage(<BrandsPage />);

    await userEvent.click(await screen.findByRole("button", { name: /Markë e re/ }));
    const dialog = await screen.findByRole("dialog");
    await userEvent.type(within(dialog).getByLabelText(/Logo/), "not a url");
    await userEvent.click(within(dialog).getByRole("button", { name: "Ruaj" }));

    expect(await within(dialog).findByText("Emri është i detyrueshëm (deri 100 shkronja).")).toBeInTheDocument();
    expect(within(dialog).getByText("Shkruani një URL të plotë (https://…).")).toBeInTheDocument();
    expect(calls.some((c) => c.method === "POST")).toBe(false);
  });

  it("explains why a brand with fragrances cannot be deleted", async () => {
    mockApi({
      "GET /api/admin/catalog/brands": () => json([lattafa]),
      "DELETE /api/admin/catalog/brands/{id}": () => problem(409, "Catalog.Brand.HasFragrances"),
    });
    renderPage(<BrandsPage />);

    await userEvent.click((await screen.findAllByRole("button", { name: "Fshi Lattafa" }))[0]!);
    await userEvent.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Fshi" }));

    expect(await screen.findByText("Marka ka parfume. Fshini ose zhvendosni parfumet më parë.")).toBeInTheDocument();
    expect(screen.getByRole("alertdialog")).toBeInTheDocument(); // stays open
  });
});
