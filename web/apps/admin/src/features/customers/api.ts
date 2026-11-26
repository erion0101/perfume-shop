import type { Schema } from "@perfumeshop/api-client";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, unwrap } from "@/lib/api";

export type CustomerListItem = Schema<"CustomerListItemDto">;
export type CustomerAdmin = Schema<"CustomerAdminDto">;

export function useCustomers(f: { search: string; status: string; hasRefusals: boolean; page: number; pageSize: number }) {
  return useQuery({
    queryKey: ["customers", "list", f],
    placeholderData: keepPreviousData,
    queryFn: () =>
      unwrap(
        api.GET("/api/admin/customers", {
          params: { query: { Search: f.search || undefined, Status: f.status || undefined, HasRefusals: f.hasRefusals || undefined, Page: f.page, PageSize: f.pageSize } },
        }),
      ),
  });
}

export function useCustomer(id: string) {
  return useQuery({ queryKey: ["customers", "detail", id], queryFn: () => unwrap(api.GET("/api/admin/customers/{id}", { params: { path: { id } } })) });
}

export function useCustomerOrders(customerId: string, status = "") {
  return useQuery({
    queryKey: ["orders", "list", { customerId, status }],
    queryFn: () => unwrap(api.GET("/api/admin/orders", { params: { query: { CustomerId: customerId, Status: status || undefined, PageSize: 100 } } })),
  });
}

export function useBlockCustomer() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, block, reason }: { id: string; block: boolean; reason: string }) =>
      block
        ? unwrap(api.POST("/api/admin/customers/{id}/block", { params: { path: { id } }, body: { reason } }))
        : unwrap(api.POST("/api/admin/customers/{id}/unblock", { params: { path: { id } }, body: { reason } })),
    onSuccess: (customer) => {
      queryClient.setQueryData(["customers", "detail", customer.id], customer);
      void queryClient.invalidateQueries({ queryKey: ["customers", "list"] });
    },
  });
}
