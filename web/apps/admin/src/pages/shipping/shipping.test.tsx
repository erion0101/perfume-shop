import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { previewCsv } from "@/lib/csv";
import { courier } from "@/test/fixtures";
import { json, mockApi, renderPage } from "@/test/utils";
import { HandoverPage } from "./HandoverPage";
import { SettlementReport } from "./SettlementsPage";
import { ManualCashDialog } from "./ShipmentDetailPage";

const missing = () =>
  new Response(
    JSON.stringify({
      status: 409,
      code: "Shipping.Shipment.ReceiptMissing",
      parcels: [{ trackingNumber: "PK9XK", orderNumber: "PS-2026-000050", receipt: "Failed" }],
    }),
    { status: 409, headers: { "Content-Type": "application/problem+json" } },
  );

describe("HandoverPage", () => {
  it("lists parcels without a receipt and hands over only with a reason", async () => {
    const calls = mockApi({
      "GET /api/admin/shipping/couriers": () => json([courier()]),
      "GET /api/admin/shipping/couriers/{id}/manifest": () => new Response("<html><body>Lista</body></html>", { headers: { "Content-Type": "text/html" } }),
      "POST /api/admin/shipping/couriers/{id}/handover": (_, body) =>
        body ? json({ date: "2026-11-26", handedOver: 1, trackingNumbers: ["PK9XK"], withoutReceipt: ["PK9XK"] }) : missing(),
    });
    renderPage(<HandoverPage />);

    await screen.findByRole("option", { name: "Posta e Kosovës" });
    await userEvent.selectOptions(screen.getByLabelText("Korrieri"), "k1");
    expect(await screen.findByTitle("Lista e dorëzimit")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /Shëno si të dorëzuara/ }));
    await userEvent.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Shëno si të dorëzuara" }));

    const dialog = await screen.findByRole("dialog", { name: "Pako pa kupon fiskal" });
    expect(within(dialog).getByText("PK9XK · PS-2026-000050 · Dështuar")).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole("button", { name: "Dorëzo pa kupon" }));
    expect(await within(dialog).findByText("Shkruani arsyen (5–500 shenja).")).toBeInTheDocument();

    await userEvent.type(within(dialog).getByLabelText(/Arsyeja/), "Shërbimi fiskal nuk punon");
    await userEvent.click(within(dialog).getByRole("button", { name: "Dorëzo pa kupon" }));

    expect(await screen.findByText(/1 pako u dorëzuan te korrieri. 1 prej tyre pa kupon fiskal/)).toBeInTheDocument();
    expect(calls.filter((c) => c.path.endsWith("/handover")).map((c) => c.body)).toEqual([null, { overrideMissingReceipt: true, overrideReason: "Shërbimi fiskal nuk punon" }]);
  });
});

describe("SettlementReport", () => {
  it("shows every problem line in Albanian and offers manual booking for wrong amounts", () => {
    mockApi({});
    renderPage(
      <SettlementReport
        settlement={{
          id: "st1",
          courierId: "k1",
          courierName: "Posta",
          reference: "W48",
          importedAtUtc: "2026-11-26T10:00:00Z",
          importedBy: "u",
          replayed: false,
          totalCollected: 100,
          totalFees: 3,
          netPaid: 97,
          matchedCount: 1,
          matchedAmount: 41.9,
          mismatches: { AmountMismatch: 1, UnknownTracking: 1, DuplicateInFile: 1 },
          lines: [
            { lineNumber: 1, trackingNumber: "PK1", orderNumber: "PS-1", collectedAmount: 41.9, expectedAmount: 41.9, fee: 1, expectedFee: 1, status: "Matched", note: null },
            { lineNumber: 2, trackingNumber: "PK2", orderNumber: "PS-2", collectedAmount: 40, expectedAmount: 41.9, fee: 1, expectedFee: 1, status: "AmountMismatch", note: null },
            { lineNumber: 3, trackingNumber: "NOPE", orderNumber: null, collectedAmount: 10, expectedAmount: null, fee: 0, expectedFee: null, status: "UnknownTracking", note: null },
            { lineNumber: 4, trackingNumber: "PK1", orderNumber: "PS-1", collectedAmount: 41.9, expectedAmount: 41.9, fee: 1, expectedFee: 1, status: "DuplicateInFile", note: null },
          ],
          deliveredButUnpaid: [],
        }}
      />,
    );

    const table = screen.getByRole("table", { name: "Probleme" });
    expect(within(table).getAllByRole("row")).toHaveLength(4); // header + 3 problems, the matched line is not repeated
    expect(within(table).getByText("Shumë e gabuar")).toBeInTheDocument();
    expect(within(table).getByText("Nr. i panjohur")).toBeInTheDocument();
    expect(within(table).getByText("Dy herë në skedar")).toBeInTheDocument();
    expect(within(table).getAllByRole("button", { name: /Regjistro manualisht/ })).toHaveLength(1);
    expect(screen.getByText("Shumë e gabuar: 1")).toBeInTheDocument();
  });
});

describe("ManualCashDialog", () => {
  const shipment = { id: "s1", trackingNumber: "PK2", codAmount: 41.9 } as Parameters<typeof ManualCashDialog>[0]["shipment"];

  it("needs a note when the amount differs from the COD amount", async () => {
    const calls = mockApi({ "POST /api/admin/shipping/shipments/{id}/cash-collected": () => json(shipment) });
    renderPage(<ManualCashDialog shipment={shipment} open onOpenChange={() => {}} />);

    const dialog = await screen.findByRole("dialog");
    const amount = within(dialog).getByLabelText(/Shuma e marrë/);
    await userEvent.clear(amount);
    await userEvent.type(amount, "40");
    await userEvent.click(within(dialog).getByRole("button", { name: "Ruaj" }));
    expect(await within(dialog).findByText("Shënimi është i detyrueshëm kur shuma ndryshon nga COD.")).toBeInTheDocument();

    await userEvent.type(within(dialog).getByLabelText(/Shënim/), "Korrieri mbajti 1,90 € për kthim");
    await userEvent.click(within(dialog).getByRole("button", { name: "Ruaj" }));
    await waitFor(() => expect(calls[0]?.body).toEqual({ amount: 40, note: "Korrieri mbajti 1,90 € për kthim" }));
  });
});

describe("previewCsv", () => {
  it("detects the delimiter, drops the BOM and counts rows", () => {
    const preview = previewCsv("﻿tracking_number;status;note\nPK1;Dorëzuar;ok\nPK2;Refuzuar;\n\n");
    expect(preview).toMatchObject({ delimiter: ";", header: ["tracking_number", "status", "note"], total: 2 });
    expect(preview.rows[1]).toEqual(["PK2", "Refuzuar", ""]);
    expect(previewCsv('a,b\n"1",2').rows[0]).toEqual(["1", "2"]);
  });
});
