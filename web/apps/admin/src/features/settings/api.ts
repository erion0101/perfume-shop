import type { Schema } from "@perfumeshop/api-client";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, unwrap } from "@/lib/api";

export type DeliveryRate = Schema<"DeliveryRateDto">;

export function useDeliveryRates() {
  return useQuery({ queryKey: ["settings", "delivery"], queryFn: () => unwrap(api.GET("/api/admin/orders/delivery-rates")) });
}

export function useSaveDeliveryRate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ countryCode, body }: { countryCode: string; body: Schema<"UpdateDeliveryRateRequest"> }) =>
      unwrap(api.PUT("/api/admin/orders/delivery-rates/{countryCode}", { params: { path: { countryCode } }, body })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ["settings", "delivery"] }),
  });
}

export function useOrderSettings() {
  return useQuery({ queryKey: ["settings", "orders"], queryFn: () => unwrap(api.GET("/api/admin/orders/settings")) });
}

export function useSaveOrderSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: Schema<"UpdateOrderSettingsRequest">) => unwrap(api.PUT("/api/admin/orders/settings", { body })),
    onSuccess: (settings) => queryClient.setQueryData(["settings", "orders"], settings),
  });
}

export function useOrdersDashboard() {
  return useQuery({ queryKey: ["orders", "dashboard"], refetchInterval: 60_000, queryFn: () => unwrap(api.GET("/api/admin/orders/dashboard")) });
}

export function usePaymentTotals() {
  return useQuery({ queryKey: ["payments", "totals"], refetchInterval: 60_000, queryFn: () => unwrap(api.GET("/api/admin/payments/totals")) });
}
