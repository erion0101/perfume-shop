import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { createElement } from "react";
import { afterEach, vi } from "vitest";

// next/navigation outside the App Router: a controllable stand-in.
export const navigation = { push: vi.fn(), replace: vi.fn(), pathname: "/" };
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: navigation.push, replace: navigation.replace, prefetch: vi.fn(), back: vi.fn() }),
  usePathname: () => navigation.pathname,
  useSearchParams: () => new URLSearchParams(),
}));

// next/image renders a plain <img> in tests.
vi.mock("next/image", () => ({
  default: (props: Record<string, unknown>) => {
    const rest = Object.fromEntries(Object.entries(props).filter(([key]) => !["fill", "priority", "sizes"].includes(key)));
    return createElement("img", rest);
  },
}));

afterEach(() => {
  cleanup();
  window.localStorage.clear();
  vi.unstubAllGlobals();
  vi.useRealTimers();
  navigation.push.mockReset();
  navigation.replace.mockReset();
  navigation.pathname = "/";
});
