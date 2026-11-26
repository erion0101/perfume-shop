import type { Schema } from "@perfumeshop/api-client";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, unwrap } from "@/lib/api";

export type StockItem = Schema<"StockItemDto">;
export type StockItemDetails = Schema<"StockItemDetailsDto">;
export type BatchTrace = Schema<"BatchTraceDto">;
export type ReceiveStockInput = Schema<"ReceiveStockRequest">;

export interface StockFilters {
  lowStock: boolean;
  outOfStock: boolean;
  sort: string;
  page: number;
  pageSize: number;
}

export const inventoryKeys = {
  list: (filters: StockFilters) => ["inventory", "items", filters] as const,
  item: (variantId: string) => ["inventory", "item", variantId] as const,
  trace: (code: string) => ["inventory", "trace", code] as const,
};

export function useStock(filters: StockFilters) {
  return useQuery({
    queryKey: inventoryKeys.list(filters),
    placeholderData: keepPreviousData,
    queryFn: () =>
      unwrap(
        api.GET("/api/admin/inventory/items", {
          params: {
            query: {
              LowStock: filters.lowStock || undefined,
              OutOfStock: filters.outOfStock || undefined,
              Sort: filters.sort || undefined,
              Page: filters.page,
              PageSize: filters.pageSize,
            },
          },
        }),
      ),
  });
}

export function useStockItem(variantId: string) {
  return useQuery({
    queryKey: inventoryKeys.item(variantId),
    queryFn: () => unwrap(api.GET("/api/admin/inventory/items/{variantId}", { params: { path: { variantId } } })),
  });
}

function useInvalidateInventory() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: ["inventory"] });
}

export function useReceiveStock() {
  const invalidate = useInvalidateInventory();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: ReceiveStockInput) => unwrap(api.POST("/api/admin/inventory/receipts", { body })),
    onSuccess: () => {
      void invalidate();
      void queryClient.invalidateQueries({ queryKey: ["catalog", "fragrances"] }); // "in stock" changed
    },
  });
}

export function useAdjustStock(variantId: string) {
  const invalidate = useInvalidateInventory();
  return useMutation({
    mutationFn: (body: Schema<"AdjustStockRequest">) => unwrap(api.POST("/api/admin/inventory/items/{variantId}/adjustments", { params: { path: { variantId } }, body })),
    onSuccess: () => void invalidate(),
  });
}

export function useSetThreshold() {
  const invalidate = useInvalidateInventory();
  return useMutation({
    mutationFn: ({ variantId, lowStockThreshold }: { variantId: string; lowStockThreshold: number }) =>
      unwrap(api.PUT("/api/admin/inventory/items/{variantId}/threshold", { params: { path: { variantId } }, body: { lowStockThreshold } })),
    onSuccess: () => void invalidate(),
  });
}

export function useBatchTrace(code: string) {
  return useQuery({
    queryKey: inventoryKeys.trace(code),
    enabled: code.trim().length >= 2,
    queryFn: () => unwrap(api.GET("/api/admin/inventory/batches", { params: { query: { Code: code.trim() } } })),
  });
}
