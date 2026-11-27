import { defineConfig, devices } from "@playwright/test";

/**
 * Storefront end-to-end: the real API (Development, own database `perfumeshop_e2e_shop` on the docker-compose
 * Postgres), a Fragella mock with six perfumes, and the storefront as a production build (next start), so ISR and
 * the API's revalidation call are exercised for real. Needs `docker compose up -d postgres` and the .NET SDK.
 */
const apiPort = 5181;
const shopPort = 3100;
const fragellaPort = 4011;

export const e2eAdmin = { email: "e2e-shop-admin@perfumeshop.test", password: "E2e-Shop-Admin-Password-1!" };
export const e2eApi = `http://127.0.0.1:${apiPort}`;
export const revalidateSecret = "e2e-storefront-revalidate-secret-0123456789";
export const smsFile = `${process.cwd()}/test-results/e2e-shop-sms.jsonl`;

export default defineConfig({
  testDir: "./e2e",
  timeout: 120_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  globalSetup: "./e2e/global-setup.ts",
  use: {
    baseURL: `http://127.0.0.1:${shopPort}`,
    locale: "sq-XK",
    timezoneId: "Europe/Belgrade",
    colorScheme: "dark",
    trace: "retain-on-failure",
  },
  projects: [
    { name: "desktop", use: { ...devices["Desktop Chrome"], viewport: { width: 1440, height: 900 } } },
    { name: "phone", use: { ...devices["Pixel 7"] } },
  ],
  webServer: [
    {
      command: "node e2e/fragella-mock.mjs",
      url: `http://127.0.0.1:${fragellaPort}/health`,
      env: { FRAGELLA_MOCK_PORT: String(fragellaPort) },
      reuseExistingServer: false,
    },
    {
      command: `dotnet run --project ../../../src/Bootstrapper/PerfumeShop.Api --no-launch-profile --urls http://127.0.0.1:${apiPort}`,
      url: `http://127.0.0.1:${apiPort}/health`,
      timeout: 240_000,
      reuseExistingServer: false,
      env: {
        ASPNETCORE_ENVIRONMENT: "Development",
        ConnectionStrings__Postgres:
          process.env.E2E_SHOP_DATABASE ?? "Host=localhost;Port=5432;Database=perfumeshop_e2e_shop;Username=perfumeshop;Password=perfumeshop",
        Auth__SeedAdmin__Email: e2eAdmin.email,
        Auth__SeedAdmin__Password: e2eAdmin.password,
        Auth__Staff__RequireTwoFactorForAdmins: "false",
        Fragella__BaseUrl: `http://127.0.0.1:${fragellaPort}/api/v1/`,
        Fragella__ApiKey: "e2e-fragella-key",
        BackgroundJobs__Enabled: "true",
        BackgroundJobs__PollingIntervalSeconds: "1",
        Notifications__DevelopmentSmsFile: smsFile,
        Customers__Otp__MaxCodesPerIp: "1000",
        // Catalog changes revalidate the storefront under test (tag "catalog").
        Storefront__RevalidateUrl: `http://127.0.0.1:${shopPort}/api/revalidate`,
        Storefront__RevalidateSecret: revalidateSecret,
      },
    },
    {
      // Production build: ISR, next/image and the revalidation route behave as in production.
      // Fresh fetch cache: an earlier run may have stopped before the API's last revalidation call arrived.
      command: `rm -rf .next/cache && pnpm build && pnpm exec next start --port ${shopPort} --hostname 127.0.0.1`,
      url: `http://127.0.0.1:${shopPort}`,
      timeout: 300_000,
      reuseExistingServer: false,
      env: {
        API_URL: e2eApi,
        SITE_URL: `http://127.0.0.1:${shopPort}`,
        REVALIDATE_SECRET: revalidateSecret,
        FEATURED_FRAGRANCE_SLUG: "lattafa-khamrah-edp",
        NEXT_TELEMETRY_DISABLED: "1",
      },
    },
  ],
});
