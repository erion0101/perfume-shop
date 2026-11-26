import { expect, test, type Page } from "@playwright/test";
import { e2eAdmin } from "../playwright.config";
import { admin, client, newCustomer, ok, order, stockedVariant, unique } from "./api";
import { totp } from "./totp";

/** Only submits the form (the next step may be a password change or a 2FA code). */
async function submitLogin(page: Page, email: string, password: string) {
  await page.goto("/login");
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Fjalëkalimi").fill(password);
  await page.getByRole("button", { name: "Hyr" }).click();
}

/** Signs in and waits for the dashboard, so the session cookie exists before the next page.goto. */
async function signIn(page: Page, email = e2eAdmin.email, password = e2eAdmin.password) {
  await submitLogin(page, email, password);
  await expect(page.getByRole("heading", { name: "Përmbledhje" })).toBeVisible();
}

async function signOut(page: Page) {
  await page.getByRole("button", { name: "Dil" }).click();
  await expect(page.getByRole("heading", { name: "Hyrja për stafin" })).toBeVisible();
}

test("staff account: created by admin → first login sets a password → enables 2FA → signs in with a code", async ({ page }) => {
  const email = `${unique("staff").toLowerCase()}@perfumeshop.test`;
  await signIn(page);

  await page.goto("/admin/staff");
  await page.getByRole("button", { name: /Anëtar i ri/ }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel(/^Emri/).fill("Blerta E2E");
  await dialog.getByLabel(/^Email/).fill(email);
  await dialog.getByRole("button", { name: /Gjenero/ }).click();
  const temporary = await dialog.getByLabel(/Fjalëkalimi i përkohshëm/).inputValue();
  await dialog.getByRole("button", { name: "Krijo" }).click();
  await expect(page.getByText("Llogaria u krijua.")).toBeVisible();
  await expect(page.getByText(email).first()).toBeVisible();
  await signOut(page);

  // First login with the temporary password: must choose a new one.
  await submitLogin(page, email, temporary);
  await expect(page.getByRole("heading", { name: "Zgjidhni fjalëkalimin tuaj" })).toBeVisible();
  const password = `Own-Pass-${Date.now()}!a`;
  await page.getByLabel(/Fjalëkalimi i ri/).fill(password);
  await page.getByLabel(/Përsëritni/).fill(password);
  await page.getByRole("button", { name: "Ruaj dhe hyr" }).click();
  await expect(page.getByRole("heading", { name: "Përmbledhje" })).toBeVisible();

  // Enable 2FA on My account with a code computed from the shown key.
  await page.goto("/account");
  await page.getByRole("button", { name: "Aktivizo 2FA" }).click();
  const key = (await page.getByTestId("authenticator-key").textContent())!;
  await page.getByLabel("Kodi 6-shifror").fill(totp(key));
  await page.getByRole("button", { name: "Aktivizo", exact: true }).click();
  const codes = page.getByRole("dialog", { name: "Kodet e rikuperimit" });
  await expect(codes.getByRole("listitem")).toHaveCount(10);
  await codes.getByRole("button", { name: "I kam ruajtur" }).click();
  await expect(page.getByText("10 kode rikuperimi të mbetura")).toBeVisible();
  await signOut(page);

  // Next sign-in asks for the authenticator code.
  await submitLogin(page, email, password);
  await expect(page.getByRole("heading", { name: "Kodi nga aplikacioni" })).toBeVisible();
  await page.getByLabel("Kodi 6-shifror").fill(totp(key));
  await page.getByRole("button", { name: "Hyr" }).click();
  await expect(page.getByRole("heading", { name: "Përmbledhje" })).toBeVisible();

  // Staff cannot open the admin-only pages.
  await page.goto("/admin/staff");
  await expect(page.getByText(/Nuk keni leje|vetëm për administrator/i)).toBeVisible();
});

test("notification template: edit with live preview and save", async ({ page }) => {
  const api = await admin();
  const original = await ok<{ body: string; transliterate: boolean; isEnabled: boolean }>(await api.get("/api/admin/notifications/templates/order-confirmed"));
  try {
    await signIn(page);
    await page.goto("/notifications/templates");
    await page.getByText("order-confirmed", { exact: true }).click();

    const preview = page.getByTestId("template-preview");
    await expect(preview).toContainText("PS-2026-000042");
    const body = page.getByLabel(/^Teksti/);
    await body.fill("{{shopName}}: porosia ");
    await page.getByRole("button", { name: "{{orderNumber}}" }).click();
    await body.press("End");
    await body.pressSequentially(" u konfirmua. Ju telefonojme para dergeses.");
    await expect(preview).toHaveText(/^.+: porosia PS-2026-000042 u konfirmua\. Ju telefonojme para dergeses\.$/);

    // An unknown placeholder is shown as an error and cannot be saved.
    await body.pressSequentially(" {{iban}}");
    await expect(page.getByText(/fushë të panjohur/)).toBeVisible();
    await expect(page.getByRole("button", { name: "Ruaj" })).toBeDisabled();
    await body.fill("{{shopName}}: porosia {{orderNumber}} u konfirmua. Ju telefonojme para dergeses.");
    await expect(page.getByRole("button", { name: "Ruaj" })).toBeEnabled();
    await page.getByRole("button", { name: "Ruaj" }).click();
    await expect(page.getByText("Shablloni u ruajt.")).toBeVisible();

    const saved = await ok<{ body: string }>(await api.get("/api/admin/notifications/templates/order-confirmed"));
    expect(saved.body).toBe("{{shopName}}: porosia {{orderNumber}} u konfirmua. Ju telefonojme para dergeses.");
  } finally {
    await api.put("/api/admin/notifications/templates/order-confirmed", { data: original });
  }
});

test("delivery fee: changed in settings, used by the next cart quote", async ({ page }) => {
  const api = await admin();
  const variantId = await stockedVariant(api);
  const rates = await ok<{ countryCode: string; isEnabled: boolean; fee: number; freeDeliveryFrom: number | null }[]>(await api.get("/api/admin/orders/delivery-rates"));
  const kosovo = rates.find((r) => r.countryCode === "XK")!;
  const quote = async () =>
    ok<{ deliveryFee: number; total: number }>(await (await client()).post("/api/cart/quote", { data: { items: [{ variantId, quantity: 1 }], countryCode: "XK" } }));

  try {
    await signIn(page);
    await page.goto("/settings/delivery");
    const row = page.getByRole("group", { name: /Kosov/ });
    await row.getByLabel("Tarifa").fill("3,50");
    await row.getByLabel("Falas nga").fill("");
    await row.getByRole("button", { name: "Ruaj" }).click();
    await expect(page.getByText(/Tarifa për .+ u ruajt\./)).toBeVisible();

    await expect.poll(async () => (await quote()).deliveryFee).toBe(3.5);
    expect((await quote()).total).toBeCloseTo(39.9 + 3.5, 2);
  } finally {
    await api.put("/api/admin/orders/delivery-rates/XK", { data: { isEnabled: kosovo.isEnabled, fee: kosovo.fee, freeDeliveryFrom: kosovo.freeDeliveryFrom } });
  }
});

test("block a customer: the customer can no longer sign in or order", async ({ page }) => {
  const api = await admin();
  const variantId = await stockedVariant(api);
  const customer = await newCustomer();
  await signIn(page);

  await page.goto("/customers");
  await page.getByPlaceholder("Telefoni ose emri").fill(customer.phone);
  await page.getByText(customer.phone).first().click();
  await expect(page).toHaveURL(new RegExp(`/customers/${customer.id}$`));
  await page.getByRole("button", { name: "Blloko" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel(/Arsyeja/).fill("E2E: refuzon çdo pako");
  await dialog.getByRole("button", { name: "Blloko" }).click();
  await expect(page.getByText("Klienti u bllokua.")).toBeVisible();
  await expect(page.getByText("E2E: refuzon çdo pako").first()).toBeVisible();

  const otp = await customer.anonymous.post("/api/auth/otp/request", { data: { phone: customer.phone } });
  expect(otp.status()).toBe(403);
  expect((await otp.json()).code).toBe("Customers.Customer.Blocked");
  const refused = await order(customer.api, variantId);
  expect(refused.status()).toBe(403);
  expect((await refused.json()).code).toBe("Orders.Customer.Blocked");

  const audit = await ok<{ items: { action: string; summary: string }[] }>(await api.get(`/api/admin/audit?EntityId=${customer.id}`));
  expect(audit.items.map((a) => a.action)).toContain("Customers.Customer.Blocked");
});
