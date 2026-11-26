import type { ApiProblem } from "@perfumeshop/api-client";
import { t } from "@/i18n";

/** A failed API call: HTTP status + the ProblemDetails `code` and field errors (FluentValidation). */
export class ApiError extends Error {
  readonly status: number;
  readonly code?: string;
  /** Keys as the API sends them ("Sku", "Lines[0].Quantity"), values = server messages. */
  readonly fieldErrors: Record<string, string[]>;
  /** The whole ProblemDetails body, including extensions (e.g. `parcels` on Shipping.Shipment.ReceiptMissing). */
  readonly problem: Record<string, unknown>;

  constructor(status: number, problem?: ApiProblem | null) {
    const body = typeof problem === "object" && problem !== null ? problem : null;
    super(body?.detail ?? body?.title ?? `HTTP ${status}`);
    this.name = "ApiError";
    this.status = status;
    this.code = body?.code ?? undefined;
    this.fieldErrors = body?.errors ?? {};
    this.problem = (body ?? {}) as Record<string, unknown>;
  }

  static network(): ApiError {
    return new ApiError(0);
  }
}

/** "Lines[0].Quantity" → "lines.0.quantity" (react-hook-form path). */
export function toFieldPath(apiKey: string): string {
  return apiKey
    .replace(/\[(\d+)\]/g, ".$1")
    .split(".")
    .map((part) => part.charAt(0).toLowerCase() + part.slice(1))
    .join(".");
}

function fieldLabel(apiKey: string): string {
  const path = toFieldPath(apiKey);
  const withoutIndex = path.replace(/\.\d+/g, "");
  const last = withoutIndex.split(".").at(-1) ?? withoutIndex;
  return t.fields[withoutIndex] ?? t.fields[last] ?? last;
}

/** A readable Albanian message for any error thrown by an API call. */
export function describeError(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return t.httpErrors.unknown;
  }

  if (error.code && t.apiErrors[error.code]) {
    return t.apiErrors[error.code]!;
  }

  const fields = Object.keys(error.fieldErrors);
  switch (true) {
    case error.status === 0:
      return t.httpErrors.network;
    case error.status === 400 && fields.length > 0:
      return t.httpErrors.validationFields([...new Set(fields.map(fieldLabel))].join(", "));
    case error.status === 400 || error.status === 422:
      return t.httpErrors.validation;
    case error.status === 401:
      return t.httpErrors.unauthorized;
    case error.status === 403:
      return t.httpErrors.forbidden;
    case error.status === 404:
      return t.httpErrors.notFound;
    case error.status === 409:
      return t.httpErrors.conflict;
    case error.status === 429:
      return t.httpErrors.tooMany;
    case error.status === 503:
      return t.httpErrors.unavailable;
    case error.status >= 500:
      return t.httpErrors.server;
    default:
      return t.httpErrors.unknown;
  }
}

/**
 * Puts the API's field errors on the matching form fields (react-hook-form `setError`).
 * Returns true when at least one field got an error.
 */
export function applyFieldErrors(error: unknown, setError: (path: never, error: { type: string; message: string }) => void, knownPaths: readonly string[]): boolean {
  if (!(error instanceof ApiError)) {
    return false;
  }

  let applied = false;
  for (const key of Object.keys(error.fieldErrors)) {
    const path = toFieldPath(key);
    if (knownPaths.some((known) => path === known || path.startsWith(`${known}.`))) {
      setError(path as never, { type: "server", message: t.httpErrors.invalidValue });
      applied = true;
    }
  }

  return applied;
}
