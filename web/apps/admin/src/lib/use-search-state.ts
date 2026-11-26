import { useCallback } from "react";
import { useSearchParams } from "react-router";

/**
 * Page state kept in the URL (?page=2&status=Draft), so filters survive reloads and can be shared.
 * Changing any filter goes back to page 1.
 */
export function useSearchState<K extends string>(defaults: Record<K, string>) {
  const [params, setParams] = useSearchParams();
  const values = Object.fromEntries(Object.keys(defaults).map((key) => [key, params.get(key) ?? defaults[key as K]])) as Record<K, string>;

  const set = useCallback(
    (changes: Partial<Record<K, string>>) => {
      setParams(
        (previous) => {
          const next = new URLSearchParams(previous);
          for (const [key, value] of Object.entries(changes) as [K, string | undefined][]) {
            if (value === undefined || value === "" || value === defaults[key]) {
              next.delete(key);
            } else {
              next.set(key, value);
            }
          }

          if (!("page" in changes)) {
            next.delete("page");
          }

          return next;
        },
        { replace: true },
      );
    },
    [setParams, defaults],
  );

  return [values, set] as const;
}
