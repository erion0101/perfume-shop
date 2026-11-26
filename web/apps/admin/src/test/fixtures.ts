/** API-shaped sample data for part-2 component tests (shapes follow the generated schema types). */
import type { Schema } from "@perfumeshop/api-client";

export const pendingOrder = (over: Partial<Schema<"OrderListItemDto">> = {}): Schema<"OrderListItemDto"> => ({
  id: "o1",
  number: "PS-2026-000042",
  status: "Pending",
  customerName: "Arbër Krasniqi",
  customerPhone: "+38344123456",
  city: "Lipjan",
  total: 41.9,
  currency: "EUR",
  itemCount: 1,
  placedAtUtc: "2026-11-26T09:00:00Z",
  confirmEndsAtUtc: "2026-11-28T09:00:00Z",
  trackingNumber: null,
  courierName: null,
  customerId: "c1",
  customerOrderCount: 1,
  customerRefusedCount: 0,
  ...over,
});

export const page = <T>(items: T[]) => ({ items, page: 1, pageSize: 100, totalCount: items.length, totalPages: 1 });

export const courier = (over: Partial<Schema<"CourierDto">> = {}): Schema<"CourierDto"> => ({
  id: "k1",
  name: "Posta e Kosovës",
  contactName: null,
  contactPhone: null,
  contactEmail: null,
  enabledCountries: ["XK"],
  deliveryFee: 2.5,
  codFeeFixed: 1,
  codFeePercent: 1,
  acceptsFlammableGoods: true,
  remittanceSchedule: "Weekly",
  remittanceDays: 7,
  notes: null,
  isEnabled: true,
  isSelectable: true,
  ...over,
});

export const orderAdmin = (status = "Confirmed"): Schema<"OrderAdminDto"> => ({
  order: {
    id: "o1",
    number: "PS-2026-000042",
    status,
    paymentMethod: "CashOnDelivery",
    currency: "EUR",
    subtotal: 39.9,
    deliveryFee: 2,
    total: 41.9,
    vatAmount: 6.39,
    customerName: "Arbër Krasniqi",
    customerPhone: "+38344123456",
    deliveryAddress: { countryCode: "XK", city: "Lipjan", street: "Rr. Adem Jashari 12", notes: null },
    customerNote: null,
    placedAtUtc: "2026-11-26T09:00:00Z",
    confirmEndsAtUtc: null,
    courierName: null,
    trackingNumber: null,
    canCancel: true,
    lines: [],
    timeline: [],
  },
  customerId: "c1",
  cancellationReason: null,
  cashCollectedAmount: null,
  notes: [],
});
