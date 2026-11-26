import { QueryClient } from "@tanstack/react-query";
import { ApiError } from "./problem";

export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        refetchOnWindowFocus: false,
        // Retry network blips, never 4xx (they will not fix themselves).
        retry: (count, error) => count < 2 && (!(error instanceof ApiError) || error.status === 0 || error.status >= 500),
      },
    },
  });
}
