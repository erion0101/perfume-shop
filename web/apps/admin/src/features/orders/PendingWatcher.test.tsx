import { act, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { page, pendingOrder } from "@/test/fixtures";
import { json, mockApi, renderPage } from "@/test/utils";
import { PendingWatcher } from "./PendingWatcher";

describe("PendingWatcher", () => {
  beforeEach(() => vi.useFakeTimers({ shouldAdvanceTime: true }));
  afterEach(() => vi.useRealTimers());

  it("shows the pending count and announces orders that arrive later", async () => {
    let orders = [pendingOrder()];
    mockApi({ "GET /api/admin/orders": () => json(page(orders)) });
    renderPage(<PendingWatcher />);

    expect(await screen.findByRole("link", { name: "Për t'u konfirmuar: 1" })).toBeInTheDocument();
    expect(screen.queryByText(/porosi e re/)).not.toBeInTheDocument(); // the first load is not "new"

    orders = [pendingOrder({ id: "o2", number: "PS-2026-000043" }), ...orders];
    await act(() => vi.advanceTimersByTimeAsync(30_000));

    expect(await screen.findByText("1 porosi e re për t'u konfirmuar")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Për t'u konfirmuar: 2" })).toBeInTheDocument();
  });
});
