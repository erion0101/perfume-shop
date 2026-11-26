import type { Schema } from "@perfumeshop/api-client";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, unwrap } from "@/lib/api";

export type AuditEntry = Schema<"AuditEntryDto">;
export type BusinessProfile = Schema<"BusinessProfileDto">;

export function useBusinessProfile() {
  return useQuery({ queryKey: ["admin", "business"], staleTime: 60_000, queryFn: () => unwrap(api.GET("/api/admin/settings/business")) });
}

export function useSaveBusinessProfile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: Schema<"UpdateBusinessProfileRequest">) => unwrap(api.PUT("/api/admin/settings/business", { body })),
    onSuccess: (profile) => queryClient.setQueryData(["admin", "business"], profile),
  });
}

export interface AuditFilters {
  action: string;
  actor: string;
  search: string;
  from: string;
  to: string;
  page: number;
  pageSize: number;
}

export function useAudit(f: AuditFilters) {
  return useQuery({
    queryKey: ["admin", "audit", f],
    placeholderData: keepPreviousData,
    queryFn: () =>
      unwrap(
        api.GET("/api/admin/audit", {
          params: {
            query: { Action: f.action || undefined, Actor: f.actor || undefined, Search: f.search || undefined, From: f.from || undefined, To: f.to || undefined, Page: f.page, PageSize: f.pageSize },
          },
        }),
      ),
  });
}
