import { Toaster as SonnerToaster, toast } from "sonner";

/** Mount once near the root. Bottom-center on phones, bottom-right on desktop. */
export function Toaster() {
  return <SonnerToaster position="bottom-right" richColors closeButton toastOptions={{ duration: 5000 }} />;
}

/** `toast.success("U ruajt")`, `toast.error(message)`, ... (sonner). */
export { toast };
