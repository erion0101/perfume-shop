import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { RouterProvider } from "react-router";
import { AppProviders } from "@/App";
import { createQueryClient } from "@/lib/query-client";
import { createRouter } from "@/router";
import "./index.css";

const queryClient = createQueryClient();
const router = createRouter();

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <AppProviders queryClient={queryClient}>
      <RouterProvider router={router} />
    </AppProviders>
  </StrictMode>,
);
