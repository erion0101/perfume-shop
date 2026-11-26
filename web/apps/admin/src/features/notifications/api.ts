import type { Schema } from "@perfumeshop/api-client";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, unwrap } from "@/lib/api";

export type Template = Schema<"TemplateDto">;
export type Message = Schema<"MessageDto">;
export type Preview = Schema<"PreviewDto">;

export function useTemplates() {
  return useQuery({ queryKey: ["notifications", "templates"], queryFn: () => unwrap(api.GET("/api/admin/notifications/templates")) });
}

export function useTemplate(key: string) {
  return useQuery({ queryKey: ["notifications", "template", key], queryFn: () => unwrap(api.GET("/api/admin/notifications/templates/{key}", { params: { path: { key } } })) });
}

export function useSaveTemplate(key: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: Schema<"UpdateTemplateRequest">) => unwrap(api.PUT("/api/admin/notifications/templates/{key}", { params: { path: { key } }, body })),
    onSuccess: (template) => {
      queryClient.setQueryData(["notifications", "template", key], template);
      void queryClient.invalidateQueries({ queryKey: ["notifications", "templates"] });
    },
  });
}

/** Live preview of a draft (nothing saved or sent). */
export function usePreview(key: string, draft: { subject: string | null; body: string; transliterate: boolean }) {
  return useQuery({
    queryKey: ["notifications", "preview", key, draft],
    placeholderData: keepPreviousData,
    retry: false,
    queryFn: () => unwrap(api.POST("/api/admin/notifications/templates/{key}/preview", { params: { path: { key } }, body: { ...draft, values: null } })),
  });
}

export interface MessageFilters {
  status: string;
  channel: string;
  templateKey: string;
  recipient: string;
  orderNumber: string;
  from: string;
  to: string;
  page: number;
  pageSize: number;
}

export function useMessages(f: MessageFilters) {
  return useQuery({
    queryKey: ["notifications", "messages", f],
    placeholderData: keepPreviousData,
    queryFn: () =>
      unwrap(
        api.GET("/api/admin/notifications/messages", {
          params: {
            query: {
              Status: f.status || undefined,
              Channel: f.channel || undefined,
              TemplateKey: f.templateKey || undefined,
              Recipient: f.recipient || undefined,
              OrderNumber: f.orderNumber || undefined,
              From: f.from || undefined,
              To: f.to || undefined,
              Page: f.page,
              PageSize: f.pageSize,
            },
          },
        }),
      ),
  });
}

export function useResend() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => unwrap(api.POST("/api/admin/notifications/messages/{id}/resend", { params: { path: { id } } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ["notifications", "messages"] }),
  });
}

export function useAlertSettings() {
  return useQuery({ queryKey: ["notifications", "alerts"], queryFn: () => unwrap(api.GET("/api/admin/notifications/settings/staff-alerts")) });
}

export function useSaveAlertSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: Schema<"UpdateStaffAlertSettingsRequest">) => unwrap(api.PUT("/api/admin/notifications/settings/staff-alerts", { body })),
    onSuccess: (settings) => queryClient.setQueryData(["notifications", "alerts"], settings),
  });
}
