import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { adminSession, json, mockApi, renderPage } from "@/test/utils";
import { AdminLayout } from "./AdminLayout";

describe("AdminLayout menu", () => {
  it("shows the catalog to Admin", async () => {
    mockApi({});
    renderPage(<AdminLayout />);

    expect(await screen.findAllByRole("link", { name: "Parfumet" })).not.toHaveLength(0);
    expect(screen.getAllByRole("link", { name: "Lista e stokut" })).not.toHaveLength(0);
    expect(screen.getByText("Administrator")).toBeInTheDocument();
  });

  it("hides the catalog from Staff", async () => {
    mockApi({ "POST /api/auth/staff/session/refresh": () => json({}, 401) });
    renderPage(<AdminLayout />, { session: { ...adminSession, roles: ["Staff"] } });

    expect(await screen.findAllByRole("link", { name: "Lista e stokut" })).not.toHaveLength(0);
    expect(screen.queryByRole("link", { name: "Parfumet" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Import nga Fragella" })).not.toBeInTheDocument();
    expect(screen.getByText("Staf")).toBeInTheDocument();
  });

  it("warns on every page while business details are placeholders (Admin gets a link)", async () => {
    mockApi({ "GET /api/admin/settings/business": () => json({ shopName: "PerfumeShop", address: "Lipjan", phone: "+383 44 000 000", email: null, taxNumber: "000000000", placeholderFields: ["phone", "taxNumber"], updatedAtUtc: "2026-11-20T10:00:00Z", updatedBy: null }) });
    renderPage(<AdminLayout />);

    expect(await screen.findByText(/të dhënat e biznesit janë ende shembull \(telefoni, numri fiskal\)/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Plotësoji tani" })).toHaveAttribute("href", "/settings/business");
  });

  it("has no warning once the details are real, and links the user name to My account", async () => {
    mockApi({ "GET /api/admin/settings/business": () => json({ shopName: "X", address: "Rr. 1", phone: "+383 44 123 456", email: null, taxNumber: "811234567", placeholderFields: [], updatedAtUtc: "2026-11-20T10:00:00Z", updatedBy: null }) });
    renderPage(<AdminLayout />);

    expect(await screen.findByRole("link", { name: /Llogaria ime: Admin/ })).toHaveAttribute("href", "/account");
    expect(screen.queryByText(/ende shembull/)).not.toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: "Stafi" })).not.toHaveLength(0);
  });

  it("tells an admin without 2FA to turn it on", async () => {
    mockApi({ "GET /api/admin/settings/business": () => json({ shopName: "X", address: "Rr. 1", phone: "+383 44 123 456", email: null, taxNumber: "811234567", placeholderFields: [], updatedAtUtc: "2026-11-20T10:00:00Z", updatedBy: null }) });
    renderPage(<AdminLayout />, { session: { ...adminSession, twoFactorEnabled: false, twoFactorSetupRequired: true } });

    expect(await screen.findByRole("link", { name: "Aktivizo 2FA" })).toHaveAttribute("href", "/account");
  });

  it("hides staff accounts and the audit log from Staff", async () => {
    mockApi({ "POST /api/auth/staff/session/refresh": () => json({}, 401), "GET /api/admin/settings/business": () => json({ shopName: "X", address: "Rr. 1", phone: "+383 44 123 456", email: null, taxNumber: "811234567", placeholderFields: ["taxNumber"], updatedAtUtc: "2026-11-20T10:00:00Z", updatedBy: null }) });
    renderPage(<AdminLayout />, { session: { ...adminSession, roles: ["Staff"] } });

    expect(await screen.findByText(/ende shembull/)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Plotësoji tani" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Regjistri i veprimeve" })).not.toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: "Klientët" })).not.toHaveLength(0);
  });
});
