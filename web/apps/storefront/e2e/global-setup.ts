import { admin } from "./api";
import { seedCatalog } from "./seed";

/** Fills the e2e database once per run (idempotent). The API then revalidates the storefront's cached pages. */
export default async function globalSetup() {
  await seedCatalog(await admin());
}
