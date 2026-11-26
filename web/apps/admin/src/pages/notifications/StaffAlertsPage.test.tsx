import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { json, mockApi, renderPage } from "@/test/utils";
import { StaffAlertsPage } from "./StaffAlertsPage";

const settings = { recipients: ["shop@perfumeshop.test"], newOrder: true, failedReceipt: true, lowStock: true, unpaidDeliveries: true, jobFailures: true, updatedBy: null, updatedAtUtc: "2026-11-20T10:00:00Z" };

describe("StaffAlertsPage", () => {
  it("validates recipients and saves the switches", async () => {
    const calls = mockApi({
      "GET /api/admin/notifications/settings/staff-alerts": () => json(settings),
      "PUT /api/admin/notifications/settings/staff-alerts": (_r, body) => json({ ...settings, ...(body as object), updatedAtUtc: "2026-11-26T10:00:00Z" }),
    });
    renderPage(<StaffAlertsPage />);

    const recipients = await screen.findByLabelText(/Marrësit/);
    await userEvent.type(recipients, "\nnot-an-email");
    expect(await screen.findByText("Kontrolloni adresat e email-it.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Ruaj" })).toBeDisabled();

    await userEvent.clear(recipients);
    await userEvent.type(recipients, "a@perfumeshop.test\nb@perfumeshop.test");
    await userEvent.click(screen.getAllByRole("checkbox")[2]!);
    await userEvent.click(screen.getByRole("button", { name: "Ruaj" }));

    await waitFor(() =>
      expect(calls.find((c) => c.method === "PUT")?.body).toEqual({
        recipients: ["a@perfumeshop.test", "b@perfumeshop.test"],
        newOrder: true,
        failedReceipt: true,
        lowStock: false,
        unpaidDeliveries: true,
        jobFailures: true,
      }),
    );
  });
});
