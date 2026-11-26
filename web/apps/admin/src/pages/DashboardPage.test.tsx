import { screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { json, mockApi, renderPage } from "@/test/utils";
import { DashboardPage } from "./DashboardPage";

const dashboard = {
  today: { date: "2026-11-26", placed: 7, confirmed: 5, shipped: 4, delivered: 3, refused: 1, cancelled: 0 },
  pendingNow: 2,
  last14Days: [
    { date: "2026-11-25", orders: 6, revenue: 251.4 },
    { date: "2026-11-26", orders: 7, revenue: 301.3 },
  ],
  topSelling30Days: [{ fragranceId: "f1", brandName: "Lattafa", fragranceName: "Khamrah", quantity: 12, revenue: 478.8 }],
  refusals30Days: { days: 30, delivered: 45, refused: 5, rate: 0.1 },
};

function api({ failedReceipts = 0, overdue = 0 } = {}) {
  mockApi({
    "GET /api/admin/orders/dashboard": () => json(dashboard),
    "GET /api/admin/payments/totals": () =>
      json({ today: "2026-11-26", collectedToday: 120.5, collectedThisWeek: 980, weekStart: "2026-11-23", outstandingAtCouriers: 340, failedReceipts }),
    "GET /api/admin/shipping/reports/cod-held": () =>
      json([{ courierId: "k1", courierName: "Posta", parcels: 8, heldAmount: 340, overdueParcels: overdue ? 2 : 0, overdueAmount: overdue, oldestDeliveryUtc: null, overdue: [] }]),
    "GET /api/admin/shipping/reports/in-transit": () => json([]),
    "GET /api/admin/inventory/items": () => json({ items: [], totalCount: 3, page: 1, pageSize: 1 }),
    "GET /api/admin/notifications/messages": () => json({ items: [], totalCount: 0, page: 1, pageSize: 1 }),
  });
}

const tile = (name: string) => screen.getByText(name).closest("a")!;

describe("DashboardPage", () => {
  it("links every number to its filtered list", async () => {
    api();
    renderPage(<DashboardPage />);

    expect(await screen.findByText("Porosi të reja")).toBeInTheDocument();
    expect(tile("Porosi të reja")).toHaveAttribute("href", "/orders?from=2026-11-26&to=2026-11-26");
    expect(tile("Për t'u konfirmuar")).toHaveAttribute("href", "/orders/queue");
    expect(tile("Refuzuar")).toHaveAttribute("href", "/orders?status=Refused");
    expect(await screen.findByText(/120,50/)).toBeInTheDocument();
    expect(tile("Mbledhur këtë javë")).toHaveAttribute("href", "/payments?status=Collected&from=2026-11-23");
    expect(within(tile("Refuzimet (30 ditë)")).getByText("10,0 %")).toBeInTheDocument();
  });

  it("shows only the alerts that need attention and highlights money overdue at couriers", async () => {
    api({ failedReceipts: 2, overdue: 79.8 });
    renderPage(<DashboardPage />);

    const alerts = await screen.findByRole("region", { name: "Sinjalizime" });
    expect(await within(alerts).findByText("Kupona të dështuar")).toBeInTheDocument();
    expect(within(alerts).getByText("Kupona të dështuar").closest("a")).toHaveAttribute("href", "/payments/receipts?status=Failed");
    expect(within(alerts).getByText("Stok i ulët").closest("a")).toHaveAttribute("href", "/inventory?low=1");
    expect(within(alerts).queryByText("Njoftime të dështuara")).not.toBeInTheDocument();
    expect(await screen.findByText(/79,80.€ pas afatit/)).toBeInTheDocument();
  });
});
