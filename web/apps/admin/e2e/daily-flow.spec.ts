import { expect, test, type Page } from "@playwright/test";
import { e2eAdmin } from "../playwright.config";
import { admin, courier, placeOrder, stockedVariant, unique } from "./api";

async function signIn(page: Page) {
  await page.goto("/login");
  await page.getByLabel("Email").fill(e2eAdmin.email);
  await page.getByLabel("Fjalëkalimi").fill(e2eAdmin.password);
  await page.getByRole("button", { name: "Hyr" }).click();
  await expect(page.getByRole("heading", { name: "Përmbledhje" })).toBeVisible();
}

const csv = (rows: string[]) => ({ name: "courier.csv", mimeType: "text/csv", buffer: Buffer.from(`${rows.join("\n")}\n`, "utf8") });

/** Order detail pages do not auto-refresh: reload until the background jobs caught up. */
async function expectOnOrderPage(page: Page, orderId: string, text: string | RegExp) {
  await expect(async () => {
    await page.goto(`/orders/${orderId}`);
    await expect(page.getByText(text).first()).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 60_000 });
}

/** Confirm in the queue → shipment with printed slip → receipt issued. Returns the tracking number. */
async function confirmAndShip(page: Page, order: { id: string; number: string }, courierName: string) {
  await page.goto("/orders/queue");
  const card = page.getByRole("listitem").filter({ hasText: order.number });
  await expect(card.getByRole("link", { name: /\+38349/ })).toHaveAttribute("href", /^tel:\+38349/);
  await card.getByRole("button", { name: `Konfirmo ${order.number}` }).click();
  await expect(page.getByText(`Porosia ${order.number} u konfirmua.`)).toBeVisible();

  await page.goto(`/orders/${order.id}`);
  await page.getByRole("button", { name: "Krijo dërgesën" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel(/^Korrieri/).selectOption({ label: courierName });
  const tracking = unique("PK");
  await dialog.getByLabel("Nr. gjurmimit").fill(tracking);
  await dialog.getByRole("button", { name: "Krijo dërgesën" }).click();

  // The packing slip opens right away, ready to print.
  const slip = page.getByRole("dialog", { name: /Fleta e pakos/ });
  await expect(slip.frameLocator("iframe").getByText("Hape pakon para se të paguash")).toBeVisible();
  await expect(slip.getByRole("button", { name: "Printo" })).toBeEnabled();
  await page.keyboard.press("Escape");

  // The sale receipt is issued by a background job a few seconds later; the page polls for it.
  await expect(page.getByRole("button", { name: "Printo kuponin" })).toBeVisible({ timeout: 60_000 });
  await page.getByRole("button", { name: "Printo kuponin" }).click();
  await expect(page.getByRole("dialog").frameLocator("iframe").getByText(/JO FISKAL/)).toBeVisible(); // development provider
  await page.keyboard.press("Escape");
  return tracking;
}

test("daily COD flow: placed → confirmed → shipped → handed over → delivered → cash collected", async ({ page }) => {
  const api = await admin();
  const [variantId, carrier] = await Promise.all([stockedVariant(api), courier(api)]);
  const order = await placeOrder(variantId);
  await signIn(page);

  const tracking = await confirmAndShip(page, order, carrier.name);

  // Daily handover: manifest shows the receipt as OK, then hand over.
  await page.goto("/shipping/handover");
  await page.getByLabel(/^Korrieri/).selectOption({ label: carrier.name });
  const manifest = page.frameLocator('iframe[title="Lista e dorëzimit"]');
  await expect(manifest.getByText(tracking)).toBeVisible();
  await expect(manifest.getByRole("row").filter({ hasText: tracking })).toContainText("OK");
  await page.getByRole("button", { name: "Shëno si të dorëzuara" }).click();
  await page.getByRole("alertdialog").getByRole("button", { name: "Shëno si të dorëzuara" }).click();
  await expect(page.getByText("1 pako u dorëzuan te korrieri.")).toBeVisible();

  // Courier status file: delivered.
  await page.goto("/shipping/status-import");
  await page.getByLabel(/^Korrieri/).selectOption({ label: carrier.name });
  await page.getByLabel("Skedari CSV").setInputFiles(csv(["tracking_number;status;note", `${tracking};Dorëzuar;Pranoi vetë`]));
  await expect(page.getByRole("table", { name: "Pamja paraprake" })).toContainText(tracking);
  await page.getByRole("button", { name: "Importo" }).click();
  await expect(page.getByRole("table", { name: "Rezultati" }).getByRole("row").filter({ hasText: tracking })).toContainText("U aplikua");

  // Courier statement: the exact amount → matched, nothing to fix.
  await page.goto("/shipping/settlements");
  await page.getByLabel(/^Korrieri/).selectOption({ label: carrier.name });
  await page.getByLabel("Referenca e pasqyrës").fill(unique("W48"));
  await page.getByLabel("Skedari CSV").setInputFiles(csv(["tracking_number;collected_amount;fee", `${tracking};${order.total.toFixed(2)};1.42`]));
  await page.getByRole("button", { name: "Importo pasqyrën" }).click();
  await expect(page.getByText("Asnjë problem: të gjitha shumat përputhen.")).toBeVisible();

  // Order CashCollected (set by Shipping right away), payment Collected with fee and net (Payments, via the outbox).
  await expectOnOrderPage(page, order.id, "Paguar");
  await expectOnOrderPage(page, order.id, /40,48\s€/); // net = 41,90 − 1,42 courier fee: only there once Payments booked the statement
});

test("refused parcel: back in the warehouse → order refused, payment failed, storno receipt", async ({ page }) => {
  const api = await admin();
  const [variantId, carrier] = await Promise.all([stockedVariant(api), courier(api)]);
  const order = await placeOrder(variantId);
  await signIn(page);
  await confirmAndShip(page, order, carrier.name);

  await page.getByRole("link", { name: "Hap dërgesën" }).click();
  const status = page.getByRole("main").locator("header").first();
  for (const [step, after] of [
    ["Dorëzo te korrieri", "Te korrieri"],
    ["Refuzuar nga klienti", "Refuzuar"],
    ["Kthyer në depo", "Kthyer në depo"],
  ] as const) {
    await page.getByRole("button", { name: step, exact: true }).click();
    const dialog = page.getByRole("dialog", { name: step });
    await dialog.getByRole("button", { name: step, exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(status.getByText(after, { exact: true })).toBeVisible(); // the shipment really moved
  }

  await expectOnOrderPage(page, order.id, "Refuzuar");
  await expectOnOrderPage(page, order.id, "Dështuar (refuzuar)");
  await expectOnOrderPage(page, order.id, /Storno PS-R-/);

  // The batch went back to stock: the trace shows the order by its number.
  const batchLink = page.getByRole("link", { name: /^B-/ }).first();
  await batchLink.click();
  await expect(page.getByRole("link", { name: order.number })).toBeVisible();
});
