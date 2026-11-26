import { expect, request, type APIRequestContext } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { e2eAdmin, e2eApi, smsFile } from "../playwright.config";

/**
 * Test data straight through the API (what the storefront will do later): products, couriers, customers, orders.
 * The UI under test is then used for the staff work.
 */
export const runId = process.env.E2E_RUN_ID!;
let counter = 0;
export const unique = (prefix: string) => `${prefix}-${runId}-${process.pid}-${++counter}`.toUpperCase();

async function client(token?: string): Promise<APIRequestContext> {
  return request.newContext({ baseURL: e2eApi, extraHTTPHeaders: token ? { Authorization: `Bearer ${token}` } : {} });
}

async function ok<T>(response: Awaited<ReturnType<APIRequestContext["get"]>>): Promise<T> {
  expect(response.ok(), `${response.url()} → ${response.status()} ${await response.text()}`).toBeTruthy();
  return (await response.json()) as T;
}

export async function admin() {
  const anonymous = await client();
  const login = await ok<{ accessToken: string }>(await anonymous.post("/api/auth/staff/login", { data: e2eAdmin }));
  return client(login.accessToken);
}

/** A published fragrance variant with 20 units in stock, at 39.90 € (free delivery from 40 € → order total 41.90 €). */
export async function stockedVariant(api: APIRequestContext) {
  const search = await ok<{ items: { fragellaId: string; importedFragranceId: string | null }[] }>(
    await api.get(`/api/admin/catalog/fragella/search?q=${encodeURIComponent(`e2e daily ${unique("q")}`)}`),
  );
  const item = search.items[0]!;
  const fragranceId =
    item.importedFragranceId ?? (await ok<{ id: string }>(await api.post("/api/admin/catalog/fragrances/import", { data: { fragellaId: item.fragellaId } }))).id;

  // One mock fragrance per run: each test sells its own size (type + size are unique per fragrance; 1-1000 ml).
  let created: Awaited<ReturnType<APIRequestContext["post"]>> | null = null;
  for (let attempt = 0; attempt < 5 && !created?.ok(); attempt++) {
    created = await api.post(`/api/admin/catalog/fragrances/${fragranceId}/variants`, {
      data: { sku: unique("E2E-D"), type: "FullBottle", size: { value: 101 + Math.floor(Math.random() * 899), unit: "Ml" }, price: 39.9 },
    });
  }

  const variant = await ok<{ id: string }>(created!);
  await ok(await api.post(`/api/admin/catalog/fragrances/${fragranceId}/variants/${variant.id}/activate`));
  await api.post(`/api/admin/catalog/fragrances/${fragranceId}/publish`); // already published by an earlier test: 409 is fine
  await ok(
    await api.post("/api/admin/inventory/receipts", {
      data: {
        supplierName: "Lattafa Distribution FZE",
        supplierInvoiceNumber: unique("INV"),
        supplierInvoiceDate: "2026-11-20",
        receivedOn: "2026-11-21",
        lines: [{ variantId: variant.id, batchCode: unique("B"), quantity: 20, unitCost: 18.5 }],
      },
    }),
  );
  return variant.id;
}

/** A courier only this test uses (so its manifest holds exactly this test's parcels). */
export async function courier(api: APIRequestContext) {
  return ok<{ id: string; name: string }>(
    await api.post("/api/admin/shipping/couriers", {
      data: {
        name: `E2E Posta ${unique("c")}`.slice(0, 60),
        enabledCountries: ["XK"],
        deliveryFee: 2.5,
        codFeeFixed: 1,
        codFeePercent: 1,
        acceptsFlammableGoods: true,
        remittanceSchedule: "Weekly",
        remittanceDays: 7,
        isEnabled: true,
      },
    }),
  );
}

async function otpCode(phone: string): Promise<string> {
  let code: string | undefined;
  await expect
    .poll(
      async () => {
        const lines = (await readFile(smsFile, "utf8").catch(() => "")).trim().split("\n").filter(Boolean);
        const last = lines.map((line) => JSON.parse(line) as { phone: string; message: string }).filter((sms) => sms.phone === phone).at(-1);
        code = last?.message.match(/\b\d{6}\b/)?.[0];
        return code;
      },
      { timeout: 15_000 },
    )
    .toBeTruthy();
  return code!;
}

/** A new customer signed in with a phone OTP (like the storefront), with a name and one address. */
export async function newCustomer() {
  const phone = `+38349${String(Date.now()).slice(-4)}${String(++counter).padStart(2, "0")}`;
  const anonymous = await client();
  await ok(await anonymous.post("/api/auth/otp/request", { data: { phone } }));
  const login = await ok<{ accessToken: string }>(await anonymous.post("/api/auth/otp/verify", { data: { phone, code: await otpCode(phone) } }));
  const api = await client(login.accessToken);
  const profile = await ok<{ id: string }>(await api.put("/api/customers/me", { data: { fullName: "Arbër E2E" } }));
  await ok(await api.post("/api/customers/me/addresses", { data: { city: "Lipjan", street: "Rr. Adem Jashari 12", notes: "kati 2" } }));
  return { phone, id: profile.id, api, anonymous };
}

export const order = (api: APIRequestContext, variantId: string) =>
  api.post("/api/orders", { data: { items: [{ variantId, quantity: 1 }] }, headers: { "Idempotency-Key": crypto.randomUUID() } });

/** A new customer who orders one bottle. Returns the order. */
export async function placeOrder(variantId: string) {
  const customer = await newCustomer();
  return ok<{ id: string; number: string; total: number }>(await order(customer.api, variantId));
}

export { client, ok };
