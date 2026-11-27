/**
 * Listing filters in the URL, with Albanian keys: /parfumet?marka=lattafa&drejtimi=Sweet&nota=vanilla&cmimi-deri=40.
 * Pure functions, used by the server page (to query the API) and the filter UI (to update the URL).
 */
export interface ListingFilters {
  brand: string[];
  gender: string;
  mood: string;
  family: string;
  note: string;
  minPrice: string;
  maxPrice: string;
  inStock: boolean;
  sort: string;
  page: number;
  q: string;
}

export const PAGE_SIZE = 24;

const keys = {
  brand: "marka",
  gender: "per",
  mood: "drejtimi",
  family: "familja",
  note: "nota",
  minPrice: "cmimi-nga",
  maxPrice: "cmimi-deri",
  inStock: "ne-stok",
  sort: "rendit",
  page: "faqja",
  q: "q",
} as const;

export const SORTS = ["newest", "price_asc", "price_desc", "name"] as const;
export const MOODS = ["Oud", "Sweet", "Fresh", "Spicy", "Floral"] as const;
export const GENDERS = ["Female", "Male", "Unisex"] as const;

type Params = URLSearchParams | Record<string, string | string[] | undefined>;

function get(params: Params, key: string): string[] {
  if (params instanceof URLSearchParams) {
    return params.getAll(key);
  }

  const value = params[key];
  return value === undefined ? [] : Array.isArray(value) ? value : [value];
}

const price = (v: string | undefined) => (v && /^\d{1,4}([.,]\d{1,2})?$/.test(v.trim()) ? v.trim().replace(",", ".") : "");

export function parseFilters(params: Params): ListingFilters {
  const one = (key: string) => get(params, key)[0]?.trim() ?? "";
  const mood = one(keys.mood);
  const gender = one(keys.gender);
  const sort = one(keys.sort);
  const page = Number.parseInt(one(keys.page), 10);
  return {
    brand: [...new Set(get(params, keys.brand).flatMap((v) => v.split(",")).map((v) => v.trim().toLowerCase()).filter((v) => /^[a-z0-9-]{1,100}$/.test(v)))],
    gender: (GENDERS as readonly string[]).includes(gender) ? gender : "",
    mood: (MOODS as readonly string[]).includes(mood) ? mood : "",
    family: /^[A-Za-z]{1,20}$/.test(one(keys.family)) ? one(keys.family) : "",
    note: one(keys.note).toLowerCase().slice(0, 100),
    minPrice: price(one(keys.minPrice)),
    maxPrice: price(one(keys.maxPrice)),
    inStock: one(keys.inStock) === "po",
    sort: (SORTS as readonly string[]).includes(sort) ? sort : "",
    page: Number.isFinite(page) && page > 1 ? Math.min(page, 500) : 1,
    q: one(keys.q).slice(0, 100),
  };
}

/** Canonical query string (empty values left out, page 1 left out), e.g. "marka=lattafa&drejtimi=Sweet". */
export function toSearch(filters: Partial<ListingFilters>): string {
  const params = new URLSearchParams();
  for (const b of filters.brand ?? []) {
    params.append(keys.brand, b);
  }

  const put = (key: string, value: string | undefined) => value && params.set(key, value);
  put(keys.gender, filters.gender);
  put(keys.mood, filters.mood);
  put(keys.family, filters.family);
  put(keys.note, filters.note);
  put(keys.minPrice, filters.minPrice);
  put(keys.maxPrice, filters.maxPrice);
  if (filters.inStock) {
    params.set(keys.inStock, "po");
  }

  put(keys.sort, filters.sort);
  if (filters.page && filters.page > 1) {
    params.set(keys.page, String(filters.page));
  }

  put(keys.q, filters.q);
  return params.toString();
}

export const hasFilters = (f: ListingFilters) =>
  f.brand.length > 0 || !!f.gender || !!f.mood || !!f.family || !!f.note || !!f.minPrice || !!f.maxPrice || f.inStock;

/**
 * The API filters one brand at a time; more than one selected brand is not offered in the UI.
 * (Kept as a list in the URL so a multi-brand filter can come later without changing links.)
 */
export const apiBrand = (f: ListingFilters) => f.brand[0];
