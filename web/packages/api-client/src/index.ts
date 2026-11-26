import createClient, { type ClientOptions } from "openapi-fetch";
import type { components, paths } from "./schema";

/**
 * Typed client for PerfumeShop.Api. Every type comes from the API's OpenAPI document
 * (openapi.json → src/schema.d.ts via `pnpm --filter @perfumeshop/api-client generate`); nothing is hand-written.
 */
export function createApiClient(options: ClientOptions) {
  return createClient<paths>(options);
}

export type ApiClient = ReturnType<typeof createApiClient>;
export type { components, paths };

/** A schema from the OpenAPI document, e.g. `Schema<"BrandDto">`. */
export type Schema<Name extends keyof components["schemas"]> = components["schemas"][Name];

/** RFC 9457 problem details as the API sends them (plus our `code` and validation `errors`). */
export type ApiProblem = Schema<"HttpValidationProblemDetails"> & { code?: string };
