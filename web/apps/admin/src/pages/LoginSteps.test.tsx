import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { json, mockApi, problem, renderPage } from "@/test/utils";
import { LoginPage } from "./LoginPage";

const session = (extra: object = {}) =>
  json({ accessToken: "abc", accessTokenExpiresAtUtc: new Date(Date.now() + 900_000).toISOString(), userId: "u1", email: "ana@perfumeshop.test", displayName: "Ana", roles: ["Staff"], twoFactorEnabled: true, twoFactorSetupRequired: false, ...extra });

async function signIn(password = "Temp-Passw0rd!") {
  await userEvent.type(await screen.findByLabelText(/Email/), "ana@perfumeshop.test");
  await userEvent.type(screen.getByLabelText(/Fjalëkalimi/), password);
  await userEvent.click(screen.getByRole("button", { name: "Hyr" }));
}

describe("LoginPage second steps", () => {
  it("asks for the authenticator code and sends it with the credentials", async () => {
    let attempts = 0;
    const calls = mockApi({
      "POST /api/auth/staff/session/refresh": () => json({}, 401),
      "POST /api/auth/staff/session": (_r, body) => ((body as { twoFactorCode: string | null }).twoFactorCode ? session() : (attempts++, problem(401, "Customers.Auth.TwoFactorRequired"))),
    });
    const { router } = renderPage(<LoginPage />, { path: "/login", route: "/login", session: null });

    await signIn();
    expect(await screen.findByText("Kodi nga aplikacioni")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Hyr" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Shkruani kodin.");

    await userEvent.type(screen.getByLabelText(/Kodi 6-shifror/), "123456");
    await userEvent.click(screen.getByRole("button", { name: "Hyr" }));

    await waitFor(() => expect(router.state.location.pathname).toBe("/"));
    expect(attempts).toBe(1);
    expect(calls.filter((c) => c.path === "/api/auth/staff/session").at(-1)!.body).toMatchObject({ twoFactorCode: "123456", recoveryCode: null });
  });

  it("switches to a recovery code", async () => {
    const calls = mockApi({
      "POST /api/auth/staff/session/refresh": () => json({}, 401),
      "POST /api/auth/staff/session": (_r, body) => ((body as { recoveryCode: string | null }).recoveryCode ? session() : problem(401, "Customers.Auth.TwoFactorRequired")),
    });
    renderPage(<LoginPage />, { path: "/login", route: "/login", session: null });

    await signIn();
    await userEvent.click(await screen.findByRole("button", { name: "Përdor një kod rikuperimi" }));
    await userEvent.type(screen.getByLabelText(/Kodi i rikuperimit/), "AB12-CD34");
    await userEvent.click(screen.getByRole("button", { name: "Hyr" }));

    await waitFor(() => expect(calls.filter((c) => c.path === "/api/auth/staff/session").at(-1)!.body).toMatchObject({ twoFactorCode: null, recoveryCode: "AB12-CD34" }));
  });

  it("makes a first-time user choose a strong password, then signs in with it", async () => {
    const calls = mockApi({
      "POST /api/auth/staff/session/refresh": () => json({}, 401),
      "POST /api/auth/staff/session": (_r, body) =>
        (body as { password: string }).password === "Brand-New-Pass1!" ? session({ twoFactorEnabled: false }) : problem(403, "Customers.Auth.PasswordChangeRequired"),
      "POST /api/auth/staff/password/initial": () => new Response(null, { status: 204 }),
    });
    const { router } = renderPage(<LoginPage />, { path: "/login", route: "/login", session: null });

    await signIn();
    expect(await screen.findByText("Zgjidhni fjalëkalimin tuaj")).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText(/Fjalëkalimi i ri/), "short");
    await userEvent.type(screen.getByLabelText(/Përsëritni/), "other");
    await userEvent.click(screen.getByRole("button", { name: "Ruaj dhe hyr" }));
    expect(calls.some((c) => c.path === "/api/auth/staff/password/initial")).toBe(false);

    await userEvent.clear(screen.getByLabelText(/Fjalëkalimi i ri/));
    await userEvent.type(screen.getByLabelText(/Fjalëkalimi i ri/), "Brand-New-Pass1!");
    await userEvent.clear(screen.getByLabelText(/Përsëritni/));
    await userEvent.type(screen.getByLabelText(/Përsëritni/), "Brand-New-Pass1!");
    await userEvent.click(screen.getByRole("button", { name: "Ruaj dhe hyr" }));

    await waitFor(() => expect(router.state.location.pathname).toBe("/"));
    expect(calls.find((c) => c.path === "/api/auth/staff/password/initial")!.body).toEqual({
      email: "ana@perfumeshop.test",
      temporaryPassword: "Temp-Passw0rd!",
      newPassword: "Brand-New-Pass1!",
      twoFactorCode: null,
    });
  });
});
