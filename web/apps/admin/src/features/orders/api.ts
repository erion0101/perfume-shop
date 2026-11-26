import type { Schema } from "@perfumeshop/api-client";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, unwrap } from "@/lib/api";

export type OrderListItem = Schema<"OrderListItemDto">;
export type OrderAdmin = Schema<"OrderAdminDto">;
export type CustomerHistory = Schema<"CustomerOrderHistoryDto">;

export interface OrderFilters {
  status: string;
  from: string;
  to: string;
  phone: string;
  number: string;
  courier: string;
  sort: string;
  page: number;
  pageSize: number;
}

/** New Pending orders appear without a reload. */
export const ORDERS_REFRESH_MS = 30_000;

const query = (f: Omit<OrderFilters, "status" | "sort" | "page" | "pageSize">) => ({
  From: f.from || undefined,
  To: f.to || undefined,
  Phone: f.phone || undefined,
  Number: f.number || undefined,
  Courier: f.courier || undefined,
});

export function useOrders(filters: OrderFilters, refetchInterval: number | false = ORDERS_REFRESH_MS) {
  return useQuery({
    queryKey: ["orders", "list", filters],
    placeholderData: keepPreviousData,
    refetchInterval,
    queryFn: () =>
      unwrap(
        api.GET("/api/admin/orders", {
          params: { query: { ...query(filters), Status: filters.status || undefined, Sort: filters.sort || undefined, Page: filters.page, PageSize: filters.pageSize } },
        }),
      ),
  });
}

export function useOrderCounts(filters: Omit<OrderFilters, "status" | "sort" | "page" | "pageSize">, refetchInterval: number | false = ORDERS_REFRESH_MS) {
  return useQuery({
    queryKey: ["orders", "counts", filters],
    placeholderData: keepPreviousData,
    refetchInterval,
    queryFn: () => unwrap(api.GET("/api/admin/orders/counts", { params: { query: query(filters) } })),
  });
}

export function useOrder(id: string) {
  return useQuery({
    queryKey: ["orders", "detail", id],
    enabled: id !== "",
    queryFn: () => unwrap(api.GET("/api/admin/orders/{id}", { params: { path: { id } } })),
  });
}

export function useCustomerHistory(customerId: string | undefined) {
  return useQuery({
    queryKey: ["orders", "history", customerId],
    enabled: Boolean(customerId),
    queryFn: () => unwrap(api.GET("/api/admin/orders/customers/{customerId}/history", { params: { path: { customerId: customerId! } } })),
  });
}

function useOrderMutation<TInput>(mutationFn: (input: TInput) => Promise<OrderAdmin>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn,
    onSuccess: (order) => {
      queryClient.setQueryData(["orders", "detail", order.order.id], order);
      void queryClient.invalidateQueries({ queryKey: ["orders"] });
    },
  });
}

export const useConfirmOrder = () => useOrderMutation((id: string) => unwrap(api.POST("/api/admin/orders/{id}/confirm", { params: { path: { id } } })));

export const useCancelOrder = () =>
  useOrderMutation(({ id, reason }: { id: string; reason: string }) => unwrap(api.POST("/api/admin/orders/{id}/cancel", { params: { path: { id } }, body: { reason } })));

export const useAddOrderNote = () =>
  useOrderMutation(({ id, text }: { id: string; text: string }) => unwrap(api.POST("/api/admin/orders/{id}/notes", { params: { path: { id } }, body: { text } })));
