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
});
