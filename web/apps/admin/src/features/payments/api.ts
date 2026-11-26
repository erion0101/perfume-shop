import type { Schema } from "@perfumeshop/api-client";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, unwrap } from "@/lib/api";

export type Payment = Schema<"PaymentDto">;
export type Receipt = Schema<"ReceiptDto">;
export type DailyCash = Schema<"DailyCashSummaryDto">;

export interface PaymentFilters {
  status: string;
  from: string;
  to: string;
  courier: string;
  orderNumber: string;
  page: number;
  pageSize: number;
}

export function usePayments(f: PaymentFilters) {
  return useQuery({
    queryKey: ["payments", "list", f],
    placeholderData: keepPreviousData,
    queryFn: () =>
      unwrap(
        api.GET("/api/admin/payments", {
          params: {
            query: {
              Status: f.status || undefined,
              From: f.from || undefined,
              To: f.to || undefined,
              Courier: f.courier || undefined,
              OrderNumber: f.orderNumber || undefined,
              Page: f.page,
              PageSize: f.pageSize,
            },
          },
        }),
      ),
  });
}

export type PaymentWithReceipts = Schema<"PaymentWithReceiptsDto">;

/**
 * Payment + receipts of an order. Payments creates them from events a few seconds after the order changes, so
 * while `waitFor` says something is still missing (no payment yet, receipt not issued yet) it polls every 3 s.
 */
export function usePaymentForOrder(orderId: string, waitFor: (data: PaymentWithReceipts | undefined) => boolean = () => false) {
  return useQuery({
    queryKey: ["payments", "order", orderId],
    enabled: orderId !== "",
    retry: false,
    refetchInterval: (query) => (waitFor(query.state.data) ? 3000 : false),
    queryFn: () => unwrap(api.GET("/api/admin/payments/orders/{orderId}", { params: { path: { orderId } } })),
  });
}

export function useDailyCash(date: string) {
  return useQuery({ queryKey: ["payments", "daily", date], queryFn: () => unwrap(api.GET("/api/admin/payments/daily-summary", { params: { query: { date: date || undefined } } })) });
}

export interface ReceiptFilters {
  status: string;
  kind: string;
  orderNumber: string;
  page: number;
  pageSize: number;
}

export function useReceipts(f: ReceiptFilters) {
  return useQuery({
    queryKey: ["payments", "receipts", f],
    placeholderData: keepPreviousData,
    queryFn: () =>
      unwrap(
        api.GET("/api/admin/payments/receipts", {
          params: { query: { Status: f.status || undefined, Kind: f.kind || undefined, OrderNumber: f.orderNumber || undefined, Page: f.page, PageSize: f.pageSize } },
        }),
      ),
  });
}

export function useReceipt(id: string) {
  return useQuery({ queryKey: ["payments", "receipt", id], queryFn: () => unwrap(api.GET("/api/admin/payments/receipts/{id}", { params: { path: { id } } })) });
}

export function useRetryReceipt() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => unwrap(api.POST("/api/admin/payments/receipts/{id}/retry", { params: { path: { id } } })),
    onSuccess: () => Promise.all(["payments", "shipping"].map((key) => queryClient.invalidateQueries({ queryKey: [key] }))),
  });
}
