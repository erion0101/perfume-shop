import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { json, mockApi, problem, renderPage } from "@/test/utils";
import { VariantDialog } from "./VariantDialog";

function setup(handlers: Parameters<typeof mockApi>[0]) {
  const calls = mockApi(handlers);
  renderPage(<VariantDialog fragranceId="f1" variant={null} open onOpenChange={() => {}} />);
  return calls;
}

describe("VariantDialog", () => {
  it("creates a variant with size and a price typed with a comma", async () => {
    const calls = setup({ "POST /api/admin/catalog/fragrances/{id}/variants": () => json({}, 201) });
    const dialog = await screen.findByRole("dialog");

    await userEvent.type(within(dialog).getByLabelText(/^SKU/), "LAT-KHA-100");
    await userEvent.type(within(dialog).getByLabelText(/^Sasia/), "100");
    await userEvent.type(within(dialog).getByLabelText(/^Çmimi \(me TVSH\)/), "39,90");
    await userEvent.click(within(dialog).getByRole("button", { name: "Ruaj" }));

    await waitFor(() => expect(calls.some((c) => c.method === "POST")).toBe(true));
    expect(calls.find((c) => c.method === "POST")!.body).toEqual({
      sku: "LAT-KHA-100",
      ean: null,
      price: 39.9,
      compareAtPrice: null,
      images: [],
      type: "FullBottle",
      size: { value: 100, unit: "Ml" },
    });
  });

  it("requires the compare-at price to be higher than the price", async () => {
    const calls = setup({});
    const dialog = await screen.findByRole("dialog");

    await userEvent.type(within(dialog).getByLabelText(/^SKU/), "X-1");
    await userEvent.type(within(dialog).getByLabelText(/^Çmimi \(me TVSH\)/), "39,90");
    await userEvent.type(within(dialog).getByLabelText(/^Çmimi i mëparshëm/), "30");
    await userEvent.type(within(dialog).getByLabelText(/^EAN/), "123");
    await userEvent.click(within(dialog).getByRole("button", { name: "Ruaj" }));

    expect(await within(dialog).findByText("Duhet të jetë më i madh se çmimi.")).toBeInTheDocument();
    expect(within(dialog).getByText("EAN ka 8, 12, 13 ose 14 shifra.")).toBeInTheDocument();
    expect(calls).toHaveLength(0);
  });

  it("marks the SKU field when the API says it is taken", async () => {
    setup({ "POST /api/admin/catalog/fragrances/{id}/variants": () => problem(409, "Catalog.Variant.SkuTaken") });
    const dialog = await screen.findByRole("dialog");

    await userEvent.type(within(dialog).getByLabelText(/^SKU/), "DUP-1");
    await userEvent.click(within(dialog).getByRole("button", { name: "Ruaj" }));

    expect(await screen.findByText("Ky SKU përdoret tashmë nga një produkt tjetër.")).toBeInTheDocument();
  });

  it("uploads a photo and adds it to the variant", async () => {
    const calls = setup({ "POST /api/admin/catalog/images": () => json({ url: "/media/catalog/2026/abc.png", contentType: "image/png", bytes: 68 }) });
    const dialog = await screen.findByRole("dialog");

    await userEvent.upload(dialog.querySelector<HTMLInputElement>('input[type="file"]')!, new File([new Uint8Array([137, 80, 78, 71])], "a.png", { type: "image/png" }));

    expect(await within(dialog).findByLabelText("Përshkrimi i fotos 1")).toBeInTheDocument();
    expect(dialog.querySelector("img")!.getAttribute("src")).toBe("http://api.test/media/catalog/2026/abc.png");
    expect(calls[0]!.headers.get("Content-Type")).toMatch(/^multipart\/form-data/);
  });
});
