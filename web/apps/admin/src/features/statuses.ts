import type { BadgeTone } from "@perfumeshop/ui";
import { t } from "@/i18n";

type BadgeMap = Record<string, { label: string; tone: BadgeTone }>;

const build = (labels: Record<string, string>, tones: Record<string, BadgeTone>): BadgeMap =>
  Object.fromEntries(Object.entries(labels).map(([status, label]) => [status, { label, tone: tones[status] ?? "neutral" }]));

export const orderBadges = build(t.enums.orderStatus, {
  Pending: "warning",
  Confirmed: "info",
  Shipped: "brand",
  Delivered: "success",
  CashCollected: "success",
  Cancelled: "neutral",
  Refused: "danger",
  Expired: "neutral",
});

export const shipmentBadges = build(t.enums.shipmentStatus, {
  Created: "warning",
  HandedOver: "info",
  InTransit: "brand",
  Delivered: "success",
  Refused: "danger",
  ReturnedToWarehouse: "neutral",
});

export const paymentBadges = build(t.enums.paymentStatus, { Expected: "info", Collected: "success", Failed: "danger", Cancelled: "neutral" });

export const receiptBadges = build(t.enums.receiptStatus, { Pending: "warning", Issued: "success", Failed: "danger", Voided: "neutral" });

export const settlementLineBadges = build(t.settlements.lineStatus, {
  Matched: "success",
  AmountMismatch: "danger",
  UnknownTracking: "danger",
  AlreadySettled: "warning",
  NotPayable: "warning",
  DuplicateInFile: "warning",
  OrderUpdateFailed: "danger",
});

export const importOutcomeBadges = build(t.statusImport.outcomes, {
  Applied: "success",
  AlreadyApplied: "neutral",
  UnknownTracking: "danger",
  InvalidTransition: "warning",
  InvalidStatus: "danger",
  ReceiptMissing: "danger",
  OrderUpdateFailed: "danger",
});
