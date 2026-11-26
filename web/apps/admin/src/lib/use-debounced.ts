import { useEffect, useState } from "react";

/** The value, `delay` ms after it stopped changing (compared as JSON). */
export function useDebounced<T>(value: T, delay: number): T {
  const [debounced, setDebounced] = useState(value);
  const json = JSON.stringify(value);
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(JSON.parse(json) as T), delay);
    return () => window.clearTimeout(timer);
  }, [json, delay]);
  return debounced;
}
