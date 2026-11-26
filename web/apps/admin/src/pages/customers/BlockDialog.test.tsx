import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { json, mockApi, renderPage } from "@/test/utils";
import { BlockDialog } from "./CustomerDetailPage";

const customer = {
  id: "c1",
  phone: "+38344111222",
  fullName: "Arta Krasniqi",
  email: null,
  status: "Active",
  blockReason: null,
  blockedAtUtc: null,
  phoneVerifiedAtUtc: "2026-11-01T10:00:00Z",
  lastLoginAtUtc: null,
  createdAtUtc: "2026-11-01T10:00:00Z",
  addresses: [],
};

describe("BlockDialog", () => {
  it("requires a reason before blocking", async () => {
    const onOpenChange = vi.fn();
    const calls = mockApi({ "POST /api/admin/customers/{id}/block": () => json({ ...customer, status: "Blocked", blockReason: "Refuzon çdo pako" }) });
    renderPage(<BlockDialog customer={customer} open onOpenChange={onOpenChange} />);

    expect(await screen.findByText("Blloko Arta Krasniqi?")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Blloko" }));
    expect(await screen.findByText("Shkruani arsyen (3–500 shenja).")).toBeInTheDocument();
    expect(calls).toHaveLength(0);

    await userEvent.type(screen.getByLabelText(/Arsyeja/), "  Refuzon çdo pako ");
    await userEvent.click(screen.getByRole("button", { name: "Blloko" }));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(calls[0]).toMatchObject({ method: "POST", path: "/api/admin/customers/c1/block", body: { reason: "Refuzon çdo pako" } });
  });

  it("unblocks a blocked customer through the unblock endpoint", async () => {
    const calls = mockApi({ "POST /api/admin/customers/{id}/unblock": () => json(customer) });
    renderPage(<BlockDialog customer={{ ...customer, status: "Blocked" }} open onOpenChange={() => {}} />);

    await userEvent.type(await screen.findByLabelText(/Arsyeja/), "Gabim i stafit");
    await userEvent.click(screen.getByRole("button", { name: "Zhblloko" }));

    await waitFor(() => expect(calls[0]?.path).toBe("/api/admin/customers/c1/unblock"));
  });
});
