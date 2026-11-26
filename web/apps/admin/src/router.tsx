import type { ComponentType } from "react";
import { createBrowserRouter, type RouteObject } from "react-router";
import { RequireAdmin, RequireAuth } from "@/components/guards";
import { AdminLayout } from "@/layouts/AdminLayout";
import { LoginPage } from "@/pages/LoginPage";
import { NotFoundPage } from "@/pages/NotFoundPage";

/** Pages load on first visit (smaller first download on phones). */
function page<M>(load: () => Promise<M>, pick: (module: M) => ComponentType, adminOnly = false): Pick<RouteObject, "lazy"> {
  return {
    lazy: async () => {
      const Page = pick(await load());
      return {
        Component: adminOnly
          ? () => (
              <RequireAdmin>
                <Page />
              </RequireAdmin>
            )
          : Page,
      };
    },
  };
}

export const routes: RouteObject[] = [
  { path: "/login", element: <LoginPage /> },
  {
    path: "/",
    element: (
      <RequireAuth>
        <AdminLayout />
      </RequireAuth>
    ),
    children: [
      { index: true, ...page(() => import("@/pages/DashboardPage"), (m) => m.DashboardPage) },
      { path: "catalog/fragrances", ...page(() => import("@/pages/catalog/FragrancesPage"), (m) => m.FragrancesPage, true) },
      { path: "catalog/fragrances/:id", ...page(() => import("@/pages/catalog/FragranceEditPage"), (m) => m.FragranceEditPage, true) },
      { path: "catalog/import", ...page(() => import("@/pages/catalog/FragellaImportPage"), (m) => m.FragellaImportPage, true) },
      { path: "catalog/brands", ...page(() => import("@/pages/catalog/BrandsPage"), (m) => m.BrandsPage, true) },
      { path: "inventory", ...page(() => import("@/pages/inventory/StockPage"), (m) => m.StockPage) },
      { path: "inventory/receive", ...page(() => import("@/pages/inventory/ReceiveStockPage"), (m) => m.ReceiveStockPage) },
      { path: "inventory/items/:variantId", ...page(() => import("@/pages/inventory/StockItemPage"), (m) => m.StockItemPage) },
      { path: "inventory/trace", ...page(() => import("@/pages/inventory/BatchTracePage"), (m) => m.BatchTracePage) },
      { path: "orders", ...page(() => import("@/pages/orders/OrdersPage"), (m) => m.OrdersPage) },
      { path: "orders/queue", ...page(() => import("@/pages/orders/QueuePage"), (m) => m.QueuePage) },
      { path: "orders/:id", ...page(() => import("@/pages/orders/OrderDetailPage"), (m) => m.OrderDetailPage) },
      { path: "shipping/shipments", ...page(() => import("@/pages/shipping/ShipmentsPage"), (m) => m.ShipmentsPage) },
      { path: "shipping/shipments/:id", ...page(() => import("@/pages/shipping/ShipmentDetailPage"), (m) => m.ShipmentDetailPage) },
      { path: "shipping/handover", ...page(() => import("@/pages/shipping/HandoverPage"), (m) => m.HandoverPage) },
      { path: "shipping/status-import", ...page(() => import("@/pages/shipping/StatusImportPage"), (m) => m.StatusImportPage) },
      { path: "shipping/settlements", ...page(() => import("@/pages/shipping/SettlementsPage"), (m) => m.SettlementsPage) },
      { path: "shipping/settlements/:id", ...page(() => import("@/pages/shipping/SettlementsPage"), (m) => m.SettlementDetailPage) },
      { path: "shipping/reports", ...page(() => import("@/pages/shipping/ReportsPage"), (m) => m.ReportsPage) },
      { path: "shipping/couriers", ...page(() => import("@/pages/shipping/CouriersPage"), (m) => m.CouriersPage) },
      { path: "payments", ...page(() => import("@/pages/payments/PaymentsPage"), (m) => m.PaymentsPage) },
      { path: "payments/daily", ...page(() => import("@/pages/payments/DailyCashPage"), (m) => m.DailyCashPage) },
      { path: "payments/receipts", ...page(() => import("@/pages/payments/ReceiptsPage"), (m) => m.ReceiptsPage) },
      { path: "payments/receipts/:id", ...page(() => import("@/pages/payments/ReceiptsPage"), (m) => m.ReceiptDetailPage) },
      { path: "*", element: <NotFoundPage /> },
    ],
  },
];

export const createRouter = () => createBrowserRouter(routes);
