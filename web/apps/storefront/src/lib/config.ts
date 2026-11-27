import "server-only";

/** PerfumeShop.Api for server-side fetches. Never exposed to the browser (no NEXT_PUBLIC_ prefix). */
export const apiUrl = (process.env.API_URL ?? "http://localhost:5080").replace(/\/$/, "");

/** Canonical origin for SEO links. */
export const siteUrl = (process.env.SITE_URL ?? "http://localhost:3000").replace(/\/$/, "");

/** Shared with the API's Storefront:RevalidateSecret. */
export const revalidateSecret = process.env.REVALIDATE_SECRET ?? "";
