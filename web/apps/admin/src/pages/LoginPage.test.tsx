import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { sessionStore } from "@/lib/session";
import { json, mockApi, problem, renderPage } from "@/test/utils";
import { LoginPage } from "./LoginPage";

const noSession = () => mockApi({ "POST /api/auth/staff/session/refresh": () => json({}, 401) });

describe("LoginPage", () => {
  it("validates the form before calling the API", async () => {
    const calls = noSession();
    renderPage(<LoginPage />, { path: "/login", route: "/login", session: null });

    await userEvent.click(await screen.findByRole("button", { name: "Hyr" }));

    expect(await screen.findByText("Shkruani një email të vlefshëm.")).toBeInTheDocument();
    expect(screen.getByText("Shkruani fjalëkalimin.")).toBeInTheDocument();
    expect(calls.some((c) => c.path === "/api/auth/staff/session" && c.method === "POST")).toBe(false);
  });

  it("shows wrong credentials in Albanian", async () => {
    mockApi({
      "POST /api/auth/staff/session/refresh": () => json({}, 401),
      "POST /api/auth/staff/session": () => problem(401, "Customers.Auth.InvalidCredentials"),
    });
    renderPage(<LoginPage />, { path: "/login", route: "/login", session: null });

    await userEvent.type(await screen.findByLabelText(/Email/), "admin@perfumeshop.test");
    await userEvent.type(screen.getByLabelText(/Fjalëkalimi/), "wrong");
    await userEvent.click(screen.getByRole("button", { name: "Hyr" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Email-i ose fjalëkalimi është gabim.");
  });

  it("keeps the access token in memory and goes to the requested page", async () => {
    const calls = mockApi({
      "POST /api/auth/staff/session/refresh": () => json({}, 401),
      "POST /api/auth/staff/session": () =>
        json({ accessToken: "abc", accessTokenExpiresAtUtc: new Date(Date.now() + 900_000).toISOString(), userId: "u1", email: "admin@perfumeshop.test", displayName: null, roles: ["Admin"], twoFactorEnabled: false, twoFactorSetupRequired: false }),
    });
    const { router } = renderPage(<LoginPage />, { path: "/login", route: "/login", session: null });

    await userEvent.type(await screen.findByLabelText(/Email/), "admin@perfumeshop.test");
    await userEvent.type(screen.getByLabelText(/Fjalëkalimi/), "secret");
    await userEvent.click(screen.getByRole("button", { name: "Hyr" }));

    await waitFor(() => expect(router.state.location.pathname).toBe("/"));
    expect(sessionStore.get()?.accessToken).toBe("abc");
    expect(calls.find((c) => c.path === "/api/auth/staff/session")!.body).toEqual({ email: "admin@perfumeshop.test", password: "secret", twoFactorCode: null, recoveryCode: null });
    expect(window.localStorage.length + window.sessionStorage.length).toBe(0); // nothing persisted in JS storage
  });
});
