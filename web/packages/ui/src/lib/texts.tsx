import { createContext, useContext, type ReactNode } from "react";

/**
 * Every text the shared components show. Apps pass their own (the admin passes Albanian from its translation
 * file); these English defaults are only a fallback for Storybook and tests.
 */
export interface UiTexts {
  loading: string;
  empty: string;
  errorTitle: string;
  retry: string;
  cancel: string;
  confirm: string;
  close: string;
  previous: string;
  next: string;
  /** e.g. "Page 2 of 5 · 93 results" */
  pageOf: (page: number, pages: number, total: number) => string;
  sortAscending: string;
  sortDescending: string;
  required: string;
}

export const defaultUiTexts: UiTexts = {
  loading: "Loading…",
  empty: "Nothing here yet.",
  errorTitle: "Something went wrong",
  retry: "Try again",
  cancel: "Cancel",
  confirm: "Confirm",
  close: "Close",
  previous: "Previous",
  next: "Next",
  pageOf: (page, pages, total) => `Page ${page} of ${pages} · ${total} results`,
  sortAscending: "Sorted ascending",
  sortDescending: "Sorted descending",
  required: "required",
};

const UiTextsContext = createContext<UiTexts>(defaultUiTexts);

export function UiTextsProvider({ texts, children }: { texts: UiTexts; children: ReactNode }) {
  return <UiTextsContext.Provider value={texts}>{children}</UiTextsContext.Provider>;
}

export function useUiTexts(): UiTexts {
  return useContext(UiTextsContext);
}
