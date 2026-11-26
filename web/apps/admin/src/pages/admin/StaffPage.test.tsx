import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { json, mockApi, problem, renderPage } from "@/test/utils";
import { StaffPage } from "./StaffPage";

const ana = { id: "s1", email: "ana@perfumeshop.test", displayName: "Ana", role: "Staff", isActive: true, mustChangePassword: true, twoFactorEnabled: false, lockedOut: false, lastLoginAtUtc: null, createdAtUtc: "2026-11-20T10:00:00Z" };

describe("StaffPage", () => {
  it("lists staff with role, first-login flag and last login", async () => {
    mockApi({ "GET /api/admin/staff": () => json([ana]) });
    renderPage(<StaffPage />);

    const row = (await screen.findAllByText("ana@perfumeshop.test"))[0]!.closest("tr")!;
    expect(within(row).getByText("Duhet të ndryshojë fjalëkalimin")).toBeInTheDocument();
    expect(within(row).getByText("Asnjëherë")).toBeInTheDocument();
  });

  it("validates a new account, generates a strong password and sends it", async () => {
    const calls = mockApi({
      "GET /api/admin/staff": () => json([]),
      "POST /api/admin/staff": (_r, body) => json({ ...ana, ...(body as object), id: "s2" }, 201),
    });
    renderPage(<StaffPage />);

    await userEvent.click(await screen.findByRole("button", { name: /Anëtar i ri/ }));
    const dialog = await screen.findByRole("dialog");
    await userEvent.type(within(dialog).getByLabelText(/Emri/), "Blerta");
    await userEvent.type(within(dialog).getByLabelText(/Email/), "Blerta@PerfumeShop.test");
    await userEvent.type(within(dialog).getByLabelText(/Fjalëkalimi i përkohshëm/), "weak");
    await userEvent.click(screen.getByRole("button", { name: "Krijo" }));
    expect(calls.some((c) => c.method === "POST")).toBe(false);

    await userEvent.click(within(dialog).getByRole("button", { name: /Gjenero/ }));
    const generated = (within(dialog).getByLabelText(/Fjalëkalimi i përkohshëm/) as HTMLInputElement).value;
    expect(generated).toMatch(/^(?=.*[A-Z])(?=.*[a-z])(?=.*\d)(?=.*[^A-Za-z0-9]).{12,}$/);
    await userEvent.selectOptions(within(dialog).getByLabelText(/Roli/), "Admin");
    await userEvent.click(screen.getByRole("button", { name: "Krijo" }));

    await waitFor(() => expect(calls.find((c) => c.method === "POST")?.body).toEqual({ displayName: "Blerta", email: "blerta@perfumeshop.test", role: "Admin", temporaryPassword: generated }));
  });

  it("shows a taken email in Albanian", async () => {
    mockApi({ "GET /api/admin/staff": () => json([]), "POST /api/admin/staff": () => problem(409, "Customers.Staff.EmailTaken") });
    renderPage(<StaffPage />);

    await userEvent.click(await screen.findByRole("button", { name: /Anëtar i ri/ }));
    const dialog = await screen.findByRole("dialog");
    await userEvent.type(within(dialog).getByLabelText(/Emri/), "Ana");
    await userEvent.type(within(dialog).getByLabelText(/Email/), "ana@perfumeshop.test");
    await userEvent.click(within(dialog).getByRole("button", { name: /Gjenero/ }));
    await userEvent.click(screen.getByRole("button", { name: "Krijo" }));

    expect(await screen.findByText(/ekziston|përdoret/i)).toBeInTheDocument();
  });
});
