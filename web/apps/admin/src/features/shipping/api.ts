import type { Schema } from "@perfumeshop/api-client";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, unwrap } from "@/lib/api";

export type Courier = Schema<"CourierDto">;
export type CourierInput = Schema<"CourierRequest">;
export type Shipment = Schema<"ShipmentDto">;
export type Settlement = Schema<"SettlementDto">;
export type StatusImportResult = Schema<"StatusImportResult">;
export type HandoverResult = Schema<"HandoverResult">;

/** Shipping changes orders, payments and stock: refresh all of them. */
function useInvalidateAll() {
  const queryClient = useQueryClient();
  return () => Promise.all(["shipping", "orders", "payments", "inventory"].map((key) => queryClient.invalidateQueries({ queryKey: [key] })));
}

export function useCouriers() {
  return useQuery({ queryKey: ["shipping", "couriers"], queryFn: () => unwrap(api.GET("/api/admin/shipping/couriers")) });
}

export function useSaveCourier() {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: CourierInput }) =>
      id ? unwrap(api.PUT("/api/admin/shipping/couriers/{id}", { params: { path: { id } }, body })) : unwrap(api.POST("/api/admin/shipping/couriers", { body })),
    onSuccess: () => void invalidate(),
  });
}

export interface ShipmentFilters {
  status: string;
  courierId: string;
  tracking: string;
  orderNumber: string;
  page: number;
  pageSize: number;
}

export function useShipments(f: ShipmentFilters, enabled = true) {
  return useQuery({
    queryKey: ["shipping", "shipments", f],
    enabled,
    placeholderData: keepPreviousData,
    queryFn: () =>
      unwrap(
        api.GET("/api/admin/shipping/shipments", {
          params: {
            query: {
              Status: f.status || undefined,
              CourierId: f.courierId || undefined,
              Tracking: f.tracking || undefined,
              OrderNumber: f.orderNumber || undefined,
              Page: f.page,
              PageSize: f.pageSize,
            },
          },
        }),
      ),
  });
}

/** The shipment of one order (orders have at most one). */
export function useShipmentForOrder(orderNumber: string | undefined) {
  const list = useShipments({ status: "", courierId: "", tracking: "", orderNumber: orderNumber ?? "", page: 1, pageSize: 5 }, Boolean(orderNumber));
  return { ...list, data: list.data?.items.find((s) => s.orderNumber === orderNumber) ?? null };
}

export function useShipment(id: string) {
  return useQuery({ queryKey: ["shipping", "shipment", id], enabled: id !== "", queryFn: () => unwrap(api.GET("/api/admin/shipping/shipments/{id}", { params: { path: { id } } })) });
}

export function useCreateShipment() {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: (body: Schema<"CreateShipmentRequest">) => unwrap(api.POST("/api/admin/shipping/shipments", { body })),
    onSuccess: () => void invalidate(),
  });
}

export type ShipmentStep = "handed-over" | "in-transit" | "delivered" | "refused" | "returned";

export function useShipmentStep() {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: ({ id, step, note, overrideReason }: { id: string; step: ShipmentStep; note?: string; overrideReason?: string }) => {
      const path = { params: { path: { id } } };
      const body = { note: note || null };
      switch (step) {
        case "handed-over":
          return unwrap(
            api.POST("/api/admin/shipping/shipments/{id}/handed-over", {
              ...path,
              body: { ...body, overrideMissingReceipt: overrideReason !== undefined, overrideReason: overrideReason ?? null },
            }),
          );
        case "in-transit":
          return unwrap(api.POST("/api/admin/shipping/shipments/{id}/in-transit", { ...path, body }));
        case "delivered":
          return unwrap(api.POST("/api/admin/shipping/shipments/{id}/delivered", { ...path, body }));
        case "refused":
          return unwrap(api.POST("/api/admin/shipping/shipments/{id}/refused", { ...path, body }));
        case "returned":
          return unwrap(api.POST("/api/admin/shipping/shipments/{id}/returned", { ...path, body }));
      }
    },
    onSuccess: () => void invalidate(),
  });
}

export function useManualCash() {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: ({ id, amount, note }: { id: string; amount: number; note: string }) =>
      unwrap(api.POST("/api/admin/shipping/shipments/{id}/cash-collected", { params: { path: { id } }, body: { amount, note: note || null } })),
    onSuccess: () => void invalidate(),
  });
}

export function useHandover() {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: ({ courierId, date, overrideReason }: { courierId: string; date?: string; overrideReason?: string }) =>
      unwrap(
        api.POST("/api/admin/shipping/couriers/{id}/handover", {
          params: { path: { id: courierId }, query: { date: date || undefined } },
          body: overrideReason === undefined ? null : { overrideMissingReceipt: true, overrideReason },
        }),
      ),
    onSuccess: () => void invalidate(),
  });
}

const csvRequest = { bodySerializer: (body: string) => body, headers: { "Content-Type": "text/csv" } };

export function useStatusImport() {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: ({ courierId, csv }: { courierId: string; csv: string }) =>
      unwrap(api.POST("/api/admin/shipping/couriers/{id}/status-imports", { params: { path: { id: courierId } }, body: csv, ...csvRequest })),
    onSuccess: () => void invalidate(),
  });
}

export function useImportSettlement() {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: ({ courierId, reference, csv }: { courierId: string; reference: string; csv: string }) =>
      unwrap(api.POST("/api/admin/shipping/couriers/{id}/settlements", { params: { path: { id: courierId }, query: { reference } }, body: csv, ...csvRequest })),
    onSuccess: () => void invalidate(),
  });
}

export function useSettlements(courierId: string) {
  return useQuery({
    queryKey: ["shipping", "settlements", courierId],
    queryFn: () => unwrap(api.GET("/api/admin/shipping/settlements", { params: { query: { courierId: courierId || undefined } } })),
  });
}

export function useSettlement(id: string) {
  return useQuery({ queryKey: ["shipping", "settlement", id], queryFn: () => unwrap(api.GET("/api/admin/shipping/settlements/{id}", { params: { path: { id } } })) });
}

export function useInTransitReport(olderThanDays: number) {
  return useQuery({
    queryKey: ["shipping", "reports", "in-transit", olderThanDays],
    queryFn: () => unwrap(api.GET("/api/admin/shipping/reports/in-transit", { params: { query: { olderThanDays } } })),
  });
}

export function useRefusalReport() {
  return useQuery({ queryKey: ["shipping", "reports", "refusals"], queryFn: () => unwrap(api.GET("/api/admin/shipping/reports/refusals", { params: { query: { top: 20 } } })) });
}

export function useCodHeldReport() {
  return useQuery({ queryKey: ["shipping", "reports", "cod-held"], queryFn: () => unwrap(api.GET("/api/admin/shipping/reports/cod-held")) });
}

/** Couriers that may carry this order: enabled, accept UN1266 and deliver to its country. */
export function couriersFor(couriers: Courier[] | undefined, countryCode: string) {
  return (couriers ?? []).filter((c) => c.isEnabled && c.acceptsFlammableGoods && c.enabledCountries.includes(countryCode));
}
