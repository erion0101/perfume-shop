import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { json, mockApi, renderPage } from "@/test/utils";
import { ReceiveStockPage, receiveSchema } from "./ReceiveStockPage";

const valid = {
  supplierName: "Lattafa Distribution FZE",
  supplierInvoiceNumber: "INV-1",
  supplierInvoiceDate: "2026-11-20",
  receivedOn: "2026-11-21",
  lines: [{ variantId: "v1", batchCode: "B2391", quantity: "10", unitCost: 18.5 }],
};

describe("receive stock rules", () => {
  it("accepts a correct invoice", () => {
    expect(receiveSchema.safeParse(valid).success).toBe(true);
  });

  it.each([
    ["no lines", { ...valid, lines: [] }, "Shtoni të paktën një rresht."],
    ["received before the invoice", { ...valid, receivedOn: "2026-11-19" }, "Data e pranimit nuk mund të jetë para faturës."],
    ["zero quantity", { ...valid, lines: [{ ...valid.lines[0]!, quantity: "0" }] }, "Sasia 1–10 000."],
    ["missing unit cost", { ...valid, lines: [{ ...valid.lines[0]!, unitCost: null }] }, "Kosto ≥ 0."],
    ["no variant", { ...valid, lines: [{ ...valid.lines[0]!, variantId: "" }] }, "Zgjidhni variantin."],
  ])("rejects %s", (_, input, message) => {
    const result = receiveSchema.safeParse(input);
    expect(result.success).toBe(false);
    expect(result.error!.issues.map((i) => i.message)).toContain(message);
  });
});

describe("ReceiveStockPage", () => {
  it("shows every missing field in Albanian without calling the API", async () => {
    const calls = mockApi({ "GET /api/admin/catalog/fragrances": () => json({ items: [], page: 1, pageSize: 20, totalCount: 0 }) });
    renderPage(<ReceiveStockPage />, { path: "/inventory/receive", route: "/inventory/receive" });

    await userEvent.click(await screen.findByRole("button", { name: "Prano mallin" }));

    expect(await screen.findByText("Shkruani furnitorin.")).toBeInTheDocument();
    expect(screen.getByText("Shkruani numrin e faturës.")).toBeInTheDocument();
    expect(screen.getByText("Zgjidhni variantin.")).toBeInTheDocument();
    expect(screen.getByText("Shkruani kodin e batch-it.")).toBeInTheDocument();
    expect(calls.some((c) => c.path === "/api/admin/inventory/receipts")).toBe(false);
  });
});
