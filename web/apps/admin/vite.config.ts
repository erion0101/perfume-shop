import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { fileURLToPath, URL } from "node:url";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) },
  },
  // Entry ≈ React, router, TanStack Query, zod (~160 kB gzip); pages are split per route.
  build: { chunkSizeWarningLimit: 600 },
  server: {
    // Calls to /api go to PerfumeShop.Api during development.
    proxy: {
      "/api": process.env.API_PROXY_TARGET ?? "http://localhost:5080",
      "/media": process.env.API_PROXY_TARGET ?? "http://localhost:5080",
    },
  },
});
