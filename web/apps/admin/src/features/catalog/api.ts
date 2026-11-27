import type { Schema } from "@perfumeshop/api-client";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, unwrap } from "@/lib/api";

export type Brand = Schema<"BrandDto">;
export type FragranceListItem = Schema<"FragranceAdminListItemDto">;
export type Fragrance = Schema<"FragranceAdminDto">;
export type Variant = Fragrance["variants"][number];
export type FragranceDetailsInput = Schema<"UpdateFragranceDetailsRequest">;
export type CreateVariantInput = Schema<"CreateVariantRequest">;
export type UpdateVariantInput = Schema<"UpdateVariantRequest">;
export type FragellaItem = Schema<"FragellaSearchItemDto">;
export type ImportInput = Schema<"ImportFromFragellaRequest">;

export interface FragranceFilters {
  search: string;
  brandId: string;
  status: string;
  inStock: string;
  sort: string;
  page: number;
  pageSize: number;
}

export const catalogKeys = {
  brands: (search = "") => ["catalog", "brands", search] as const,
  fragrances: (filters: FragranceFilters) => ["catalog", "fragrances", filters] as const,
  fragrance: (id: string) => ["catalog", "fragrance", id] as const,
  usage: ["catalog", "fragella-usage"] as const,
  fragella: (q: string) => ["catalog", "fragella", q] as const,
};

export function useBrands(search = "") {
  return useQuery({
    queryKey: catalogKeys.brands(search),
    queryFn: () => unwrap(api.GET("/api/admin/catalog/brands", { params: { query: { Search: search || undefined } } })),
  });
}

export function useSaveBrand() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: Schema<"CreateBrandRequest"> }) =>
      id
        ? unwrap(api.PUT("/api/admin/catalog/brands/{id}", { params: { path: { id } }, body }))
        : unwrap(api.POST("/api/admin/catalog/brands", { body })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["catalog", "brands"] }),
  });
}

export function useDeleteBrand() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => unwrap(api.DELETE("/api/admin/catalog/brands/{id}", { params: { path: { id } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["catalog", "brands"] }),
  });
}

export function useFragrances(filters: FragranceFilters) {
  return useQuery({
    queryKey: catalogKeys.fragrances(filters),
    placeholderData: keepPreviousData,
    queryFn: () =>
      unwrap(
        api.GET("/api/admin/catalog/fragrances", {
          params: {
            query: {
              Search: filters.search || undefined,
              BrandId: filters.brandId || undefined,
              Status: filters.status || undefined,
              InStock: filters.inStock === "" ? undefined : filters.inStock === "true",
              Sort: filters.sort || undefined,
              Page: filters.page,
              PageSize: filters.pageSize,
            },
          },
        }),
      ),
  });
}

export function useFragrance(id: string) {
  return useQuery({
    queryKey: catalogKeys.fragrance(id),
    enabled: id !== "",
    queryFn: () => unwrap(api.GET("/api/admin/catalog/fragrances/{id}", { params: { path: { id } } })),
  });
}

/** Every fragrance mutation returns the whole fragrance: put it in the cache directly. */
function useFragranceMutation<TInput>(mutationFn: (input: TInput) => Promise<Fragrance>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn,
    onSuccess: (fragrance) => {
      queryClient.setQueryData(catalogKeys.fragrance(fragrance.id), fragrance);
      void queryClient.invalidateQueries({ queryKey: ["catalog", "fragrances"] });
    },
  });
}

export function useUpdateFragrance(id: string) {
  return useFragranceMutation((body: FragranceDetailsInput) => unwrap(api.PUT("/api/admin/catalog/fragrances/{id}", { params: { path: { id } }, body })));
}

export function usePublish(id: string) {
  return useFragranceMutation((publish: boolean) =>
    publish
      ? unwrap(api.POST("/api/admin/catalog/fragrances/{id}/publish", { params: { path: { id } } }))
      : unwrap(api.POST("/api/admin/catalog/fragrances/{id}/unpublish", { params: { path: { id } } })),
  );
}

export function useSaveVariant(fragranceId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ variantId, create, update }: { variantId?: string; create?: CreateVariantInput; update?: UpdateVariantInput }) =>
      variantId
        ? unwrap(api.PUT("/api/admin/catalog/fragrances/{id}/variants/{variantId}", { params: { path: { id: fragranceId, variantId } }, body: update! }))
        : unwrap(api.POST("/api/admin/catalog/fragrances/{id}/variants", { params: { path: { id: fragranceId } }, body: create! })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: catalogKeys.fragrance(fragranceId) }),
  });
}

export function useVariantActive(fragranceId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ variantId, active }: { variantId: string; active: boolean }) =>
      active
        ? unwrap(api.POST("/api/admin/catalog/fragrances/{id}/variants/{variantId}/activate", { params: { path: { id: fragranceId, variantId } } }))
        : unwrap(api.POST("/api/admin/catalog/fragrances/{id}/variants/{variantId}/deactivate", { params: { path: { id: fragranceId, variantId } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: catalogKeys.fragrance(fragranceId) }),
  });
}

export function useUploadImage() {
  return useMutation({
    mutationFn: (file: File) =>
      unwrap(
        api.POST("/api/admin/catalog/images", {
          body: { file: file as unknown as string },
          bodySerializer: () => {
            const form = new FormData();
            form.append("file", file);
            return form;
          },
        }),
      ),
  });
}

export function useFragellaUsage(enabled = true) {
  return useQuery({ queryKey: catalogKeys.usage, enabled, queryFn: () => unwrap(api.GET("/api/admin/catalog/fragella/usage")) });
}

export function useFragellaSearch(q: string) {
  const queryClient = useQueryClient();
  return useQuery({
    queryKey: catalogKeys.fragella(q),
    enabled: q.trim().length >= 2,
    staleTime: Infinity, // each search costs a Fragella request (unless the API has it cached)
    queryFn: async () => {
      const result = await unwrap(api.GET("/api/admin/catalog/fragella/search", { params: { query: { Q: q.trim() } } }));
      void queryClient.invalidateQueries({ queryKey: catalogKeys.usage });
      return result;
    },
  });
}

export function useImportFragrance() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: ImportInput) => unwrap(api.POST("/api/admin/catalog/fragrances/import", { body })),
    onSuccess: (fragrance) => {
      queryClient.setQueryData(catalogKeys.fragrance(fragrance.id), fragrance);
      void queryClient.invalidateQueries({ queryKey: ["catalog"] });
    },
  });
}

/** The full PUT body from a loaded fragrance (the endpoint replaces everything). */
export function toDetailsInput(f: Fragrance): FragranceDetailsInput {
  return {
    name: f.name,
    slug: f.slug,
    concentration: f.concentration as FragranceDetailsInput["concentration"],
    gender: f.gender as FragranceDetailsInput["gender"],
    olfactiveFamily: (f.olfactiveFamily ?? null) as FragranceDetailsInput["olfactiveFamily"],
    launchYear: f.launchYear ?? null,
    perfumers: f.perfumers,
    description: f.description ?? null,
    longevity: (f.longevity ?? null) as FragranceDetailsInput["longevity"],
    sillage: (f.sillage ?? null) as FragranceDetailsInput["sillage"],
    seasons: f.seasons as FragranceDetailsInput["seasons"],
    occasions: f.occasions as FragranceDetailsInput["occasions"],
    notes: f.notes,
    accords: f.accords,
    similarTo: f.similarTo,
    // Always sent: leaving it out would clear the admin's mood choice on every other save.
    moodOverride: (f.moodOverride ?? null) as FragranceDetailsInput["moodOverride"],
  };
}
