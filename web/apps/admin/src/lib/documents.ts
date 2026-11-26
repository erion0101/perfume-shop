import { api, unwrap } from "./api";

/** Printable HTML documents the API renders (they need the staff token, so they are fetched, not linked). */
export type PrintableDocument =
  | { kind: "packing-slip"; shipmentId: string }
  | { kind: "receipt"; receiptId: string }
  | { kind: "manifest"; courierId: string; date?: string };

export function fetchDocument(document: PrintableDocument): Promise<string> {
  switch (document.kind) {
    case "packing-slip":
      return unwrap(api.GET("/api/admin/shipping/shipments/{id}/packing-slip", { params: { path: { id: document.shipmentId } }, parseAs: "text" }));
    case "receipt":
      return unwrap(api.GET("/api/admin/payments/receipts/{id}/print", { params: { path: { id: document.receiptId } }, parseAs: "text" }));
    case "manifest":
      return unwrap(
        api.GET("/api/admin/shipping/couriers/{id}/manifest", { params: { path: { id: document.courierId }, query: { date: document.date || undefined } }, parseAs: "text" }),
      );
  }
}

/** Courier manifest as CSV (UTF-8 BOM, semicolons), saved as a file. */
export async function downloadManifestCsv(courierId: string, date?: string) {
  const blob = await unwrap(
    api.GET("/api/admin/shipping/couriers/{id}/manifest", { params: { path: { id: courierId }, query: { date: date || undefined, format: "csv" } }, parseAs: "blob" }),
  );
  const url = URL.createObjectURL(blob as Blob);
  const link = window.document.createElement("a");
  link.href = url;
  link.download = `manifest-${date || new Date().toISOString().slice(0, 10)}.csv`;
  link.click();
  URL.revokeObjectURL(url);
}
