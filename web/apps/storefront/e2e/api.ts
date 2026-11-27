import { expect, request, type APIRequestContext } from "@playwright/test";
import { e2eAdmin, e2eApi } from "../playwright.config";

/** Test data through the API, the way the admin panel creates it. */
export async function client(token?: string): Promise<APIRequestContext> {
  return request.newContext({ baseURL: e2eApi, extraHTTPHeaders: token ? { Authorization: `Bearer ${token}` } : {} });
}

export async function ok<T>(response: Awaited<ReturnType<APIRequestContext["get"]>>): Promise<T> {
  expect(response.ok(), `${response.url()} → ${response.status()} ${await response.text()}`).toBeTruthy();
  return (await response.json()) as T;
}

export async function admin() {
  const login = await ok<{ accessToken: string }>(await (await client()).post("/api/auth/staff/login", { data: e2eAdmin }));
  return client(login.accessToken);
}
