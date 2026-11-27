import { expect, test, type Page } from "@playwright/test";
import { admin, client, ok } from "./api";

const isPhone = (page: Page) => (page.viewportSize()?.width ?? 1440) < 768;

/** Cached (ISR) pages are rebuilt after the API's revalidation call: reload until the new content is there. */
async function gotoUntil(page: Page, url: string, ready: (page: Page) => Promise<void>) {
  await expect(async () => {
    await page.goto(url);
    await ready(page);
  }).toPass({ timeout: 60_000, intervals: [1_000, 2_000, 3_000] });
}

const card = (page: Page, name: string) => page.getByRole("article").filter({ has: page.getByRole("heading", { name, exact: true }) });

async function slugOf(name: string) {
  const list = await ok<{ items: { name: string; slug: string }[] }>(await (await client()).get(`/api/catalog/fragrances?Q=${encodeURIComponent(name)}`));
  return list.items.find((i) => i.name === name)!.slug;
}

test.beforeEach(async ({ page }) => {
  // A fresh cart per test, but kept across reloads within the test (sessionStorage survives a reload).
  await page.addInitScript(() => {
    if (!window.sessionStorage.getItem("e2e-started")) {
      window.localStorage.clear();
      window.sessionStorage.setItem("e2e-started", "1");
    }
  });
});

test("home: hero, real free-delivery threshold and the mood filter", async ({ page }) => {
  await gotoUntil(page, "/", async (p) => expect(card(p, "Khamrah")).toBeVisible({ timeout: 2_000 }));

  await expect(page.getByRole("heading", { level: 1, name: "Aroma që mbetet pasi ti ke ikur." })).toBeVisible();
  await expect(page.getByText(/falas mbi 40,00\s€/)).toBeVisible(); // from Orders' delivery options, not hard-coded
  await expect(page.getByRole("heading", { name: "Lattafa Khamrah" })).toBeVisible(); // Aroma e javës
  await expect(page.getByText("Kanellë, arrë muskati, bergamot")).toBeVisible(); // pyramid, notes in Albanian

  const group = page.getByRole("group", { name: "Drejtimi i aromës" });
  await expect(group.getByRole("button", { name: "Lule" })).toHaveCount(0); // no floral perfume to sell → no chip
  await group.getByRole("button", { name: "Freski" }).click();
  await expect(group.getByRole("button", { name: "Freski" })).toHaveAttribute("aria-pressed", "true");
  await expect(page.getByText("2 aroma")).toBeVisible();
  await expect(card(page, "Hawas for Him")).toBeVisible();
  await expect(card(page, "Khamrah")).toHaveCount(0);

  await group.getByRole("button", { name: "Të gjitha" }).click();
  await expect(card(page, "Khamrah")).toBeVisible();
});

test("listing: filters live in the URL, chips remove them, empty state", async ({ page }) => {
  await gotoUntil(page, "/parfumet", async (p) => expect(card(p, "Asad")).toBeVisible({ timeout: 2_000 }));
  await expect(page.getByText("6 parfume")).toBeVisible();

  const panel = isPhone(page) ? page.getByRole("dialog", { name: "Filtrat" }) : page.getByRole("complementary", { name: "Filtrat" });
  if (isPhone(page)) {
    await page.getByRole("button", { name: "Filtro" }).click();
  }

  await panel.getByRole("radio", { name: /Erëza dhe duhan/ }).check();
  await expect(page).toHaveURL(/drejtimi=Spicy/);
  if (isPhone(page)) {
    await panel.getByRole("button", { name: /Shfaq 1 parfum/ }).click();
  }

  await expect(page.getByText("1 parfum", { exact: true })).toBeVisible();
  await expect(card(page, "Asad")).toBeVisible();
  await expect(card(page, "Asad").getByText("S'ka në stok")).toBeVisible();

  // In stock only: Asad is sold out → empty state.
  if (isPhone(page)) {
    await page.getByRole("button", { name: "Filtro" }).click();
  }

  await panel.getByRole("checkbox", { name: "Vetëm në stok" }).check();
  await expect(page).toHaveURL(/ne-stok=po/);
  if (isPhone(page)) {
    await page.keyboard.press("Escape");
  }

  await expect(page.getByText("Asnjë parfum me këto filtra.")).toBeVisible();

  // The chip removes one filter; the URL is the state (reload keeps it).
  await page.getByRole("link", { name: "Hiq filtrin Erëza dhe duhan" }).click();
  await expect(page).not.toHaveURL(/drejtimi=/);
  await page.reload();
  await expect(page.getByText("5 parfume")).toBeVisible();
  await expect(page.getByRole("link", { name: "Hiq filtrin Vetëm në stok" })).toBeVisible();
});

test("product: variant picker, note pyramid, add to cart → drawer with totals from the quote", async ({ page }) => {
  const slug = await slugOf("Khamrah");
  await page.goto(`/parfum/${slug}`);

  await expect(page.getByRole("heading", { level: 1, name: "Khamrah" })).toBeVisible();
  await expect(page.getByText("Në fillim")).toBeVisible();
  await expect(page.getByText("Hurma, pralinë, tuberozë")).toBeVisible();
  await expect(page.getByRole("list", { name: "Akordet kryesore" }).getByText("Ëmbël")).toBeVisible();
  await expect(page.getByRole("meter", { name: /Qëndrueshmëria: E gjatë/ })).toBeVisible();
  // Starts on the cheapest size in stock (30 ml); the 100 ml bottle shows its saving (44.90 → 39.90).
  await expect(page.getByRole("radio", { name: "30 ml · Mini" })).toBeChecked();
  await page.getByText("100 ml", { exact: true }).click();
  await expect(page.getByText(/Kursen 5,00.€/)).toBeVisible();

  await page.getByText("30 ml · Mini").click();
  await expect(page.getByText(/19,90.€/).first()).toBeVisible();
  await page.getByRole("button", { name: "Shto në shportë" }).first().click();

  const drawer = page.getByRole("dialog", { name: "Shporta" });
  await expect(drawer.getByRole("link", { name: "Lattafa Khamrah" })).toBeVisible();
  await expect(drawer.getByText(/Edhe 20,10\s€ dhe dërgesa është falas/)).toBeVisible(); // 40 − 19.90, from the quote
  await expect(drawer.getByText(/^21,90.€$/)).toBeVisible(); // 19.90 + 2.00 delivery

  await drawer.getByRole("button", { name: "Një më shumë Lattafa Khamrah" }).click();
  await drawer.getByRole("button", { name: "Një më shumë Lattafa Khamrah" }).click();
  await expect(drawer.getByText("Dërgesa jote është falas.")).toBeVisible();
  await expect(drawer.getByText(/^59,70.€$/).last()).toBeVisible();

  // Saved in the browser: after a reload the cart button still counts 3.
  await page.keyboard.press("Escape");
  await expect(drawer).toBeHidden();
  await page.reload();
  await expect(page.getByRole("button", { name: "Shporta, 3 artikuj" })).toBeVisible();
});

test("product out of stock cannot be added", async ({ page }) => {
  await page.goto(`/parfum/${await slugOf("Asad")}`);

  await expect(page.getByRole("status").filter({ hasText: "S'ka në stok" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Shto në shportë" }).first()).toBeDisabled();
});

test("search: typo-tolerant suggestions, keyboard pick, full results", async ({ page }) => {
  await page.goto("/kerko");
  const box = page.getByRole("combobox", { name: "Kërko parfum ose markë" });
  await box.fill("kamrah");

  const option = page.getByRole("option", { name: /Lattafa Khamrah/ });
  await expect(option).toBeVisible();
  await box.press("ArrowDown");
  await expect(option).toHaveAttribute("aria-selected", "true");
  await box.press("Enter");
  await expect(page.getByRole("heading", { level: 1, name: "Khamrah" })).toBeVisible();

  await page.goto("/kerko?q=lataffa");
  await expect(page.getByRole("heading", { name: "Rezultatet për „lataffa\"" })).toBeVisible();
  await expect(card(page, "Yara")).toBeVisible();
});

test("brands: list and brand page with its whole range", async ({ page }) => {
  await gotoUntil(page, "/markat", async (p) => expect(p.getByRole("link", { name: /Lattafa/ })).toBeVisible({ timeout: 2_000 }));
  await page.getByRole("link", { name: /Lattafa/ }).click();

  await expect(page.getByRole("heading", { level: 1, name: "Lattafa" })).toBeVisible();
  await expect(page.getByText("4 parfume")).toBeVisible();
  await expect(card(page, "Bade'e Al Oud Amethyst")).toBeVisible();
});

test("a price change in the admin reaches the cached pages through revalidation", async ({ page }, info) => {
  test.skip(info.project.name !== "desktop", "once is enough");
  const api = await admin();
  const slug = await slugOf("Yara");
  const list = await ok<{ items: { id: string; slug: string }[] }>(await api.get(`/api/admin/catalog/fragrances?Search=Yara`));
  const id = list.items.find((i) => i.slug === slug)!.id;
  const fragrance = await ok<{ variants: { id: string; sku: string; price: { amount: number } }[] }>(await api.get(`/api/admin/catalog/fragrances/${id}`));
  const variant = fragrance.variants[0]!;
  const original = variant.price.amount;
  const changed = original === 25.5 ? 26.5 : 25.5;
  const shown = (amount: number) => new RegExp(`${amount.toFixed(2).replace(".", ",")}.€`);
  const setPrice = async (price: number) => ok(await api.put(`/api/admin/catalog/fragrances/${id}/variants/${variant.id}`, { data: { sku: variant.sku, price } }));

  try {
    await setPrice(changed);
    await gotoUntil(page, `/parfum/${slug}`, async (p) => expect(p.getByText(shown(changed)).first()).toBeVisible({ timeout: 2_000 }));
    await gotoUntil(page, "/parfumet", async (p) => expect(card(p, "Yara").getByText(shown(changed))).toBeVisible({ timeout: 2_000 }));
  } finally {
    await setPrice(original);
  }

  await gotoUntil(page, `/parfum/${slug}`, async (p) => expect(p.getByText(shown(original)).first()).toBeVisible({ timeout: 2_000 }));
});
