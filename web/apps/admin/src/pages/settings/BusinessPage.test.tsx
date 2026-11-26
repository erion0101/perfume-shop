import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { adminSession, json, mockApi, renderPage } from "@/test/utils";
import { BusinessPage } from "./BusinessPage";

const seeded = { shopName: "PerfumeShop", address: "Lipjan, Kosovë", phone: "+383 44 000 000", email: null, taxNumber: "000000000", placeholderFields: ["phone", "taxNumber"], updatedAtUtc: "2026-11-20T10:00:00Z", updatedBy: null };

describe("BusinessPage", () => {
  it("marks placeholder fields, validates the fiscal number and saves", async () => {
    const calls = mockApi({
      "GET /api/admin/settings/business": () => json(seeded),
      "PUT /api/admin/settings/business": (_r, body) => json({ ...seeded, ...(body as object), placeholderFields: [], updatedBy: "admin@perfumeshop.test", updatedAtUtc: "2026-11-26T10:00:00Z" }),
    });
    renderPage(<BusinessPage />);

    expect(await screen.findAllByText(/Vlerë shembull/)).toHaveLength(2);
    const tax = screen.getByLabelText(/Numri fiskal/);
    await userEvent.clear(tax);
    await userEvent.type(tax, "12AB");
    await userEvent.click(screen.getByRole("button", { name: "Ruaj" }));
    expect(await screen.findByText("8–10 shifra.", { selector: "[id$='-error'], p" })).toBeInTheDocument();
    expect(calls.some((c) => c.method === "PUT")).toBe(false);

    await userEvent.clear(tax);
    await userEvent.type(tax, "811234567");
    const phone = screen.getByLabelText(/Telefoni/);
    await userEvent.clear(phone);
    await userEvent.type(phone, "+383 44 123 456");
    await userEvent.click(screen.getByRole("button", { name: "Ruaj" }));

    await waitFor(() =>
      expect(calls.find((c) => c.method === "PUT")?.body).toEqual({ shopName: "PerfumeShop", address: "Lipjan, Kosovë", phone: "+383 44 123 456", email: null, taxNumber: "811234567" }),
    );
    expect(await screen.findByText(/Ndryshuar së fundi nga admin@perfumeshop.test/)).toBeInTheDocument();
  });

  it("is read-only for Staff", async () => {
    mockApi({ "GET /api/admin/settings/business": () => json(seeded) });
    renderPage(<BusinessPage />, { session: { ...adminSession, roles: ["Staff"] } });

    expect(await screen.findByLabelText(/Emri i dyqanit/)).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Ruaj" })).not.toBeInTheDocument();
  });
});
