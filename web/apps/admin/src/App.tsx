import { Toaster, UiTextsProvider } from "@perfumeshop/ui";
import { QueryClientProvider, type QueryClient } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { t } from "@/i18n";
import { AuthProvider } from "@/lib/auth";

/** Everything a page needs around it (also used by the component tests). */
export function AppProviders({ queryClient, children }: { queryClient: QueryClient; children: ReactNode }) {
  return (
    <QueryClientProvider client={queryClient}>
      <UiTextsProvider texts={t.ui}>
        <AuthProvider>
          {children}
          <Toaster />
        </AuthProvider>
      </UiTextsProvider>
    </QueryClientProvider>
  );
}
