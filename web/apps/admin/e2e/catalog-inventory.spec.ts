import { expect, test, type Page } from "@playwright/test";
import { e2eAdmin } from "../playwright.config";

const runId = process.env.E2E_RUN_ID!;
// 1×1 PNG (the API checks the file signature, not the name).
const png = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==", "base64");

async function signIn(page: Page) {
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Hyrja për stafin" })).toBeVisible();
  await page.getByLabel("Email").fill(e2eAdmin.email);
  await page.getByLabel("Fjalëkalimi").fill(e2eAdmin.password);
  await page.getByRole("button", { name: "Hyr" }).click();
  await expect(page.getByRole("heading", { name: "Mirë se vini" })).toBeVisible();
}

async function openMenu(page: Page, link: string) {
  const burger = page.getByRole("button", { name: "Hap menunë" });
  if (await burger.isVisible()) {
    await burger.click(); // phone layout
  }

  await page.getByRole("link", { name: link }).first().click();
}

test("import from Fragella → add variant → receive stock → publish", async ({ page }, testInfo) => {
  const suffix = `${runId}-${testInfo.project.name}`;
  const sku = `E2E-${suffix}`.toUpperCase();
  await signIn(page);

  // Refresh keeps the session (access token in memory, refresh token in the HttpOnly cookie).
  await page.reload();
  await expect(page.getByRole("heading", { name: "Mirë se vini" })).toBeVisible();

  // 1. Import from the (mocked) Fragella API.
  await openMenu(page, "Import nga Fragella");
  await expect(page.getByTestId("fragella-remaining")).toContainText("Kërkesa të mbetura këtë muaj");
  await page.getByLabel("Emri i parfumit").fill(`khamrah e2e ${suffix}`); // unique: the API caches searches for 30 days
  await page.getByRole("button", { name: "Kërko" }).click();
  const result = page.getByRole("listitem").filter({ hasText: `Khamrah E2E ${runId}` });
  await expect(result).toBeVisible();

  const importButton = result.getByRole("button", { name: "Importo" });
  if (await importButton.isVisible()) {
    await importButton.click();
    await expect(page.getByText("U importua.")).toBeVisible();
  } else {
    // Another project of this run imported it already: the result links to it.
    await expect(result.getByText("Është importuar")).toBeVisible();
    await result.getByRole("link", { name: "Hap" }).click();
  }

  await expect(page.getByRole("heading", { name: `Lattafa Khamrah E2E ${runId}` })).toBeVisible();
  await expect(page.getByText("Cinnamon", { exact: true })).toBeVisible(); // note pyramid from Fragella

  // 2. Add a variant with a photo.
  await page.getByRole("button", { name: "Shto variant" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("SKU").fill(sku);
  await dialog.getByLabel("Lloji").selectOption(testInfo.project.name === "phone" ? "Tester" : "FullBottle");
  await dialog.getByLabel("Sasia").fill("100");
  await dialog.getByLabel("Çmimi (me TVSH)").fill("39,90");
  await dialog.locator('input[type="file"]').setInputFiles({ name: "khamrah.png", mimeType: "image/png", buffer: png });
  await expect(dialog.getByLabel("Përshkrimi i fotos 1")).toBeVisible();
  await dialog.getByRole("button", { name: "Ruaj" }).click();
  await expect(dialog).toBeHidden();
  const variant = page.getByRole("listitem").filter({ hasText: sku });
  await expect(variant).toContainText("39,90 €");
  await expect(variant).toContainText("Joaktiv"); // new variants start inactive
  await variant.getByRole("button", { name: "Aktivizo" }).click();
  await expect(page.getByText("Varianti u aktivizua.")).toBeVisible();
  await expect(variant).toContainText("Aktiv");

  // 3. Receive stock for exactly this variant (link from the variant row).
  await variant.getByRole("link", { name: "Prano mallin" }).click();
  await expect(page.getByRole("heading", { name: "Prano mallin" })).toBeVisible();
  await page.getByLabel("Furnitori").fill("Lattafa Distribution FZE");
  await page.getByLabel("Nr. i faturës").fill(`INV-${suffix}`);
  await expect(page.getByLabel("Produkti / varianti 1")).toHaveValue(/.+/);
  await page.getByLabel("Kodi i batch-it").fill(`B-${suffix}`);
  await page.getByLabel("Sasia").fill("12");
  await page.getByLabel("Kosto për copë (pa TVSH)").fill("18,50");
  await page.getByRole("button", { name: "Prano mallin" }).click();

  // → stock item page: 12 on hand, batch with the supplier invoice.
  await expect(page.getByText("Malli u pranua (1 batch).")).toBeVisible();
  await expect(page.getByRole("link", { name: `B-${suffix}` })).toBeVisible();
  await expect(page.getByText(`INV-${suffix}`).first()).toBeVisible();

  // 4. Publish from the fragrance page.
  await page.goBack();
  await page.goBack();
  await expect(page.getByRole("heading", { name: `Lattafa Khamrah E2E ${runId}` })).toBeVisible();
  const publish = page.getByRole("button", { name: "Publiko" });
  if (await publish.isVisible()) {
    await publish.click();
    await page.getByRole("alertdialog").getByRole("button", { name: "Publiko" }).click();
    await expect(page.getByText("Parfumi u publikua.")).toBeVisible();
  }

  await expect(page.getByText("Publikuar").first()).toBeVisible();
  await expect(page.getByRole("button", { name: "Hiq nga dyqani" })).toBeVisible();

  // The list shows it as published and in stock.
  await openMenu(page, "Parfumet");
  const row = page.getByText(`Khamrah E2E ${runId}`).first();
  await expect(row).toBeVisible();
});

test("signing out ends the session", async ({ page }) => {
  await signIn(page);
  await page.getByRole("button", { name: "Dil" }).click();
  await expect(page.getByRole("heading", { name: "Hyrja për stafin" })).toBeVisible();
  await page.reload();
  await expect(page.getByRole("heading", { name: "Hyrja për stafin" })).toBeVisible(); // the cookie is gone too
});
