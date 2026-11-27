import { expect, test } from "@playwright/test";
import { client, ok } from "./api";

/** Review screenshots (not a test): SCREENSHOTS=1 pnpm e2e screenshots → docs/screenshots/phase-1/. */
test.skip(!process.env.SCREENSHOTS, "set SCREENSHOTS=1 to capture review screenshots");

const out = (name: string, project: string) => `../../../docs/screenshots/phase-1/${name}-${project}.png`;

test("capture key pages", async ({ page }, info) => {
  test.setTimeout(180_000);
  const items = await ok<{ items: { name: string; slug: string; brandSlug: string }[] }>(await (await client()).get("/api/catalog/fragrances?PageSize=48"));
  const khamrah = items.items.find((i) => i.name === "Khamrah")!;
  const shots: [string, string][] = [
    ["home", "/"],
    ["listing", "/parfumet"],
    ["listing-filtered", "/parfumet?drejtimi=Sweet"],
    ["product", `/parfum/${khamrah.slug}`],
    ["brands", "/markat"],
    ["brand", `/markat/${khamrah.brandSlug}`],
    ["search", "/kerko?q=lataffa"],
  ];

  for (const [name, url] of shots) {
    await page.goto(url, { waitUntil: "load" });
    await page.evaluate(() => document.fonts.ready);
    await page.waitForTimeout(400);
    await page.screenshot({ path: out(name, info.project.name), fullPage: true, animations: "disabled" });
  }

  // Cart drawer open after adding Khamrah.
  await page.goto(`/parfum/${khamrah.slug}`);
  await page.getByRole("button", { name: "Shto në shportë" }).first().click();
  await expect(page.getByRole("dialog", { name: "Shporta" }).getByText("Totali", { exact: true })).toBeVisible();
  await page.screenshot({ path: out("cart-drawer", info.project.name), animations: "disabled" });

  // Search suggestions open.
  await page.goto("/kerko");
  await page.getByRole("combobox").fill("kamrah");
  await expect(page.getByRole("option").first()).toBeVisible();
  await page.screenshot({ path: out("search-suggestions", info.project.name), animations: "disabled" });

  if (info.project.name === "phone") {
    await page.goto("/parfumet");
    await page.getByRole("button", { name: "Filtro" }).click();
    await page.screenshot({ path: out("filters-drawer", info.project.name), animations: "disabled" });
    await page.keyboard.press("Escape");
    await page.getByRole("button", { name: "Hap menunë" }).click();
    await page.screenshot({ path: out("menu", info.project.name), animations: "disabled" });
  }
});
