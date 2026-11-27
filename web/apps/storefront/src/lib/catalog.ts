import "server-only";
import type { Schema } from "@perfumeshop/api-client";
import { apiUrl } from "./config";

export type FragranceCard = Schema<"FragranceListItemDto">;
export type FragranceDetails = Schema<"FragranceDetailsDto">;
export type FragrancePage = Schema<"PagedResponseOfFragranceListItemDto">;
export type ShopBrand = Schema<"ShopBrandDto">;
export type ShopBrandDetails = Schema<"ShopBrandDetailsDto">;
export type ShopFacets = Schema<"ShopFacetsDto">;
export type DeliveryOption = Schema<"DeliveryOptionDto">;

/**
 * Cache tag the API revalidates after every catalog change (Storefront:RevalidateUrl → /api/revalidate).
 * Pages also refresh on a timer, because stock changes in Inventory do not send a revalidation.
 */
export const CATALOG_TAG = "catalog";
export const CATALOG_REVALIDATE_SECONDS = 300;

export class NotFoundError extends Error {}

async function get<T>(path: string, query?: Record<string, string | number | boolean | undefined | null>): Promise<T> {
  const url = new URL(`${apiUrl}${path}`);
  for (const [key, value] of Object.entries(query ?? {})) {
    if (value !== undefined && value !== null && value !== "") {
      url.searchParams.set(key, String(value));
    }
  }

  const response = await fetch(url, { next: { tags: [CATALOG_TAG], revalidate: CATALOG_REVALIDATE_SECONDS }, headers: { Accept: "application/json" } });
  if (response.status === 404) {
    throw new NotFoundError(path);
  }

  if (!response.ok) {
    throw new Error(`GET ${path} → ${response.status}`);
  }

  return (await response.json()) as T;
}

export interface ListingQuery {
  brand?: string;
  gender?: string;
  family?: string;
  mood?: string;
  note?: string;
  minPrice?: number;
  maxPrice?: number;
  inStock?: boolean;
  q?: string;
  sort?: string;
  page?: number;
  pageSize?: number;
}

export const listFragrances = (query: ListingQuery = {}) =>
  get<FragrancePage>("/api/catalog/fragrances", {
    Brand: query.brand,
    Gender: query.gender,
    Family: query.family,
    Mood: query.mood,
    Note: query.note,
    MinPrice: query.minPrice,
    MaxPrice: query.maxPrice,
    InStock: query.inStock ? true : undefined,
    Q: query.q,
    Sort: query.sort,
    Page: query.page,
    PageSize: query.pageSize,
  });

export const getFragrance = (slug: string) => get<FragranceDetails>(`/api/catalog/fragrances/${encodeURIComponent(slug)}`);

export const getSimilar = (slug: string, limit = 8) => get<FragranceCard[]>(`/api/catalog/fragrances/${encodeURIComponent(slug)}/similar`, { limit });

export const listBrands = () => get<ShopBrand[]>("/api/catalog/brands");

export const getBrand = (slug: string) => get<ShopBrandDetails>(`/api/catalog/brands/${encodeURIComponent(slug)}`);

export const getFacets = () => get<ShopFacets>("/api/catalog/facets");

export const getDeliveryOptions = () => get<DeliveryOption[]>("/api/cart/delivery-options");

/** Every listed fragrance (for static params and the sitemap). The catalog is a few hundred products. */
export async function listAllFragrances(): Promise<FragranceCard[]> {
  const pageSize = 48;
  const first = await listFragrances({ sort: "name", pageSize, page: 1 });
  const pages = Math.ceil(first.totalCount / pageSize);
  const rest = await Promise.all(Array.from({ length: Math.max(0, pages - 1) }, (_, i) => listFragrances({ sort: "name", pageSize, page: i + 2 })));
  return [first, ...rest].flatMap((p) => p.items);
}

/** A fetch that may fail at build time (API down) should not break the whole page. */
export async function orEmpty<T>(promise: Promise<T>, fallback: T): Promise<T> {
  try {
    return await promise;
  } catch (error) {
    if (error instanceof NotFoundError) {
      throw error;
    }

    console.error(error);
    return fallback;
  }
}
