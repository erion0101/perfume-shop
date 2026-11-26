import { describe, expect, it } from "vitest";
import { json } from "@/test/utils";
import { mockApi } from "@/test/utils";
import { api, unwrap } from "./api";
import { ApiError } from "./problem";
import { sessionStore } from "./session";

const session = (token: string) => ({ accessToken: token, accessTokenExpiresAtUtc: new Date(Date.now() + 900_000).toISOString(), userId: "u1", email: "a@b.c", displayName: null, roles: ["Staff"], twoFactorEnabled: false, twoFactorSetupRequired: false });

describe("authFetch", () => {
  it("sends the bearer token and the client header", async () => {
    sessionStore.set({ accessToken: "t1", expiresAt: Date.now() + 1e6, userId: "u", email: "e", displayName: null, roles: ["Staff"], twoFactorEnabled: false, twoFactorSetupRequired: false });
    const calls = mockApi({ "GET /api/admin/catalog/brands": () => json([]) });

    await unwrap(api.GET("/api/admin/catalog/brands"));

    expect(calls[0]!.headers.get("Authorization")).toBe("Bearer t1");
    expect(calls[0]!.headers.get("X-PerfumeShop-Client")).toBe("admin");
  });

  it("refreshes once on 401 and repeats the requests with the new token", async () => {
    sessionStore.set({ accessToken: "old", expiresAt: Date.now() + 1e6, userId: "u", email: "e", displayName: null, roles: ["Staff"], twoFactorEnabled: false, twoFactorSetupRequired: false });
    let refreshes = 0;
    const calls = mockApi({
      "GET /api/admin/catalog/brands": (request) => (request.headers.get("Authorization") === "Bearer new" ? json([{ id: "b1" }]) : json({}, 401)),
      "POST /api/auth/staff/session/refresh": () => {
        refreshes++;
        return json(session("new"));
      },
    });

    const [a, b] = await Promise.all([unwrap(api.GET("/api/admin/catalog/brands")), unwrap(api.GET("/api/admin/catalog/brands"))]);

    expect(a).toEqual([{ id: "b1" }]);
    expect(b).toEqual([{ id: "b1" }]);
    expect(refreshes).toBe(1); // rotation: two refreshes would revoke the session
    expect(sessionStore.get()?.accessToken).toBe("new");
    expect(calls.filter((c) => c.path === "/api/admin/catalog/brands")).toHaveLength(4);
  });

  it("signs out when the refresh cookie is no longer valid", async () => {
    sessionStore.set({ accessToken: "old", expiresAt: Date.now() + 1e6, userId: "u", email: "e", displayName: null, roles: ["Staff"], twoFactorEnabled: false, twoFactorSetupRequired: false });
    mockApi({
      "GET /api/admin/catalog/brands": () => json({ code: "x" }, 401),
      "POST /api/auth/staff/session/refresh": () => json({ code: "Customers.Auth.InvalidRefreshToken" }, 401),
    });

    await expect(unwrap(api.GET("/api/admin/catalog/brands"))).rejects.toMatchObject({ status: 401 });
    expect(sessionStore.get()).toBeNull();
  });

  it("turns network failures into ApiError(0)", async () => {
    mockApi({
      "GET /api/admin/catalog/brands": () => {
        throw new TypeError("Failed to fetch");
      },
    });

    const error = await unwrap(api.GET("/api/admin/catalog/brands")).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(0);
  });
});
