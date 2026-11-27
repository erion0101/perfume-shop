import type { APIRequestContext } from "@playwright/test";
import { ok } from "./api";

type AdminFragrance = {
  id: string;
  slug: string;
  name: string;
  status: string;
  concentration: string;
  gender: string;
  olfactiveFamily: string | null;
  launchYear: number | null;
  perfumers: string[];
  description: string | null;
  notes: unknown;
  accords: unknown;
  longevity: string | null;
  sillage: string | null;
  seasons: string[];
  occasions: string[];
  similarTo: unknown[];
  moodOverride: string | null;
  variants: { id: string; sku: string; isActive: boolean }[];
};

type Variant = { sku: string; type: string; size?: { value: number; unit: string }; price: number; compareAtPrice?: number; stock: number };

/** The six perfumes of the home prototype. Prices are test data. Asad is out of stock on purpose. */
export const products: { key: string; description: string; seasons: string[]; variants: Variant[] }[] = [
  {
    key: "khamrah",
    description: "E ngrohtë dhe e ëmbël, si kafe me hurma pranë zjarrit. Për mbrëmje dimri dhe për ata që duan të mbahen mend.",
    seasons: ["Autumn", "Winter"],
    variants: [
      { sku: "SF-LAT-KHA-100", type: "FullBottle", size: { value: 100, unit: "Ml" }, price: 39.9, compareAtPrice: 44.9, stock: 20 },
      { sku: "SF-LAT-KHA-30", type: "Mini", size: { value: 30, unit: "Ml" }, price: 19.9, stock: 10 },
    ],
  },
  { key: "yara", description: "E butë, pudrore dhe frutore, me vanilje në fund. E lehtë për çdo ditë.", seasons: ["Spring", "Summer"], variants: [{ sku: "SF-LAT-YAR-100", type: "FullBottle", size: { value: 100, unit: "Ml" }, price: 27.9, stock: 15 }] },
  { key: "cdni", description: "Limon, ananas dhe thupër e tymosur: e freskët në fillim, e fortë deri në mbrëmje.", seasons: ["Spring", "Summer"], variants: [{ sku: "SF-ARM-CDN-105", type: "FullBottle", size: { value: 105, unit: "Ml" }, price: 34.9, stock: 12 }] },
  { key: "hawas", description: "Mollë, kardamom dhe ambergris: freski detare për ditët e nxehta.", seasons: ["Summer"], variants: [{ sku: "SF-RAS-HAW-100", type: "FullBottle", size: { value: 100, unit: "Ml" }, price: 36.9, stock: 8 }] },
  { key: "asad", description: "Piper i zi, duhan dhe vanilje: erëza të ngrohta për mbrëmje.", seasons: ["Autumn", "Winter"], variants: [{ sku: "SF-LAT-ASA-100", type: "FullBottle", size: { value: 100, unit: "Ml" }, price: 29.9, stock: 0 }] },
  { key: "amethyst", description: "Trëndafil, oud dhe myshk: e thellë dhe e qetë.", seasons: ["Autumn", "Winter"], variants: [{ sku: "SF-LAT-AME-100", type: "FullBottle", size: { value: 100, unit: "Ml" }, price: 32.9, stock: 9 }] },
];

const Admin = "/api/admin/catalog";

/** Idempotent: an existing database keeps its products (re-runs only fill in what is missing). */
export async function seedCatalog(api: APIRequestContext) {
  for (const product of products) {
    const search = await ok<{ items: { fragellaId: string; importedFragranceId: string | null }[] }>(await api.get(`${Admin}/fragella/search?q=${product.key}`));
    const hit = search.items[0]!;
    const id = hit.importedFragranceId ?? (await ok<{ id: string }>(await api.post(`${Admin}/fragrances/import`, { data: { fragellaId: hit.fragellaId } }))).id;
    let fragrance = await ok<AdminFragrance>(await api.get(`${Admin}/fragrances/${id}`));

    if (fragrance.description !== product.description) {
      const details = {
        name: fragrance.name, slug: fragrance.slug, concentration: fragrance.concentration, gender: fragrance.gender,
        olfactiveFamily: fragrance.olfactiveFamily, launchYear: fragrance.launchYear, perfumers: fragrance.perfumers,
        longevity: fragrance.longevity, sillage: fragrance.sillage, occasions: fragrance.occasions, notes: fragrance.notes,
        accords: fragrance.accords, similarTo: fragrance.similarTo, moodOverride: fragrance.moodOverride,
      };
      fragrance = await ok<AdminFragrance>(await api.put(`${Admin}/fragrances/${id}`, { data: { ...details, description: product.description, seasons: product.seasons } }));
    }

    for (const v of product.variants) {
      let variant = fragrance.variants.find((x) => x.sku === v.sku);
      if (!variant) {
        variant = await ok(await api.post(`${Admin}/fragrances/${id}/variants`, { data: { sku: v.sku, type: v.type, size: v.size, price: v.price, compareAtPrice: v.compareAtPrice } }));
        if (v.stock > 0) {
          await ok(
            await api.post("/api/admin/inventory/receipts", {
              data: {
                supplierName: "E2E Distribution",
                supplierInvoiceNumber: `E2E-${v.sku}`,
                supplierInvoiceDate: "2026-11-20",
                receivedOn: "2026-11-21",
                lines: [{ variantId: variant!.id, batchCode: `B-${v.sku}`, quantity: v.stock, unitCost: 15 }],
              },
            }),
          );
        }
      }

      if (!variant!.isActive) {
        await ok(await api.post(`${Admin}/fragrances/${id}/variants/${variant!.id}/activate`));
      }
    }

    if (fragrance.status !== "Published") {
      await ok(await api.post(`${Admin}/fragrances/${id}/publish`));
    }
  }
}
