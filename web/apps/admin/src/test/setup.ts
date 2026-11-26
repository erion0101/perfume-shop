import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach, vi } from "vitest";
import { sessionStore } from "@/lib/session";

afterEach(() => {
  cleanup();
  sessionStore.set(null);
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});
