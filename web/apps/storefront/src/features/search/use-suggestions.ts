"use client";

import type { Schema } from "@perfumeshop/api-client";
import { useEffect, useState } from "react";
import { useDebounced } from "@/lib/use-debounced";

export type Suggestions = Schema<"SearchSuggestionsDto">;

export const MIN_QUERY = 2;
export const DEBOUNCE_MS = 200;

/** Instant suggestions for what is typed (debounced; older answers never overwrite newer ones). */
export function useSuggestions(query: string) {
  const debounced = useDebounced(query.trim(), DEBOUNCE_MS);
  // The latest answer, for the query it answers; state is only set from the fetch callbacks.
  const [result, setResult] = useState<{ query: string; data: Suggestions | null; error: boolean }>({ query: "", data: null, error: false });
  const active = debounced.length >= MIN_QUERY;

  useEffect(() => {
    if (!active) {
      return;
    }

    const controller = new AbortController();
    fetch(`/api/catalog/search/suggestions?q=${encodeURIComponent(debounced)}`, { signal: controller.signal, headers: { Accept: "application/json" } })
      .then((r) => (r.ok ? (r.json() as Promise<Suggestions>) : Promise.reject(new Error(String(r.status)))))
      .then(
        (data) => setResult({ query: debounced, data, error: false }),
        () => !controller.signal.aborted && setResult({ query: debounced, data: null, error: true }),
      );
    return () => controller.abort();
  }, [debounced, active]);

  const current = active && result.query === debounced;
  return {
    data: current ? result.data : null,
    error: current && result.error,
    loading: active && !current,
    pending: query.trim() !== debounced || (active && !current),
  };
}
