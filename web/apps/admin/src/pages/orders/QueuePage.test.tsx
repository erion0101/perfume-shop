import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { orderAdmin, page, pendingOrder } from "@/test/fixtures";
import { json, mockApi, renderPage } from "@/test/utils";
import { QueuePage } from "./QueuePage";

const history = { customerId: "c2", total: 3, completed: 1, refused: 1, cancelled: 0, open: 1, recent: [] };

describe("QueuePage", () => {
  it("lists pending orders oldest first with a tap-to-call link and the refusal risk", async () => {
    const calls = mockApi({
      "GET /api/admin/orders": () => json(page([pendingOrder(), pendingOrder({ id: "o2", number: "PS-2026-000043", customerId: "c2", customerPhone: "+38349111222", customerOrderCount: 3, customerRefusedCount: 1 })])),
      "GET /api/admin/orders/customers/{id}/history": () => json(history),
    });
    renderPage(<QueuePage />);

    const cards = await screen.findAllByRole("listitem");
    expect(within(cards[0]!).getByRole("link", { name: /\+38344123456/ })).toHaveAttribute("href", "tel:+38344123456");
    expect(within(cards[0]!).getByText("Porosia e parë")).toBeInTheDocument();
    expect(within(cards[1]!).getByText("Rrezik: 1 pako të refuzuara")).toBeInTheDocument();
    expect(await within(cards[1]!).findByText("Të refuzuara")).toBeInTheDocument(); // customer history
    const list = calls.find((c) => c.path === "/api/admin/orders")!;
    expect(list).toBeDefined();
  });

  it("asks the API for Pending sorted oldest first", async () => {
    const urls: string[] = [];
    mockApi({
      "GET /api/admin/orders": (request) => {
        urls.push(request.url);
        return json(page([]));
      },
    });
    renderPage(<QueuePage />);

    expect(await screen.findByText("Nuk ka porosi për t'u konfirmuar.")).toBeInTheDocument();
    expect(urls[0]).toContain("Status=Pending");
    expect(urls[0]).toContain("Sort=placed");
  });

  it("confirms an order", async () => {
    const calls = mockApi({
      "GET /api/admin/orders": () => json(page([pendingOrder()])),
      "POST /api/admin/orders/{id}/confirm": () => json(orderAdmin("Confirmed")),
    });
    renderPage(<QueuePage />);

    await userEvent.click(await screen.findByRole("button", { name: "Konfirmo PS-2026-000042" }));

    expect(await screen.findByText("Porosia PS-2026-000042 u konfirmua.")).toBeInTheDocument();
    expect(calls.some((c) => c.method === "POST" && c.path === "/api/admin/orders/o1/confirm")).toBe(true);
  });

  it("requires a reason to cancel and sends it", async () => {
    const calls = mockApi({
      "GET /api/admin/orders": () => json(page([pendingOrder()])),
      "POST /api/admin/orders/{id}/cancel": () => json(orderAdmin("Cancelled")),
    });
    renderPage(<QueuePage />);

    await userEvent.click(await screen.findByRole("button", { name: "Anulo PS-2026-000042" }));
    const dialog = await screen.findByRole("dialog");
    await userEvent.click(within(dialog).getByRole("button", { name: "Anulo" }));
    expect(await within(dialog).findByText("Shkruani arsyen (3–500 shenja).")).toBeInTheDocument();

    await userEvent.type(within(dialog).getByLabelText(/Arsyeja/), "Klienti nuk përgjigjet");
    await userEvent.click(within(dialog).getByRole("button", { name: "Anulo" }));

    await waitFor(() => expect(calls.find((c) => c.method === "POST")?.body).toEqual({ reason: "Klienti nuk përgjigjet" }));
  });
});
