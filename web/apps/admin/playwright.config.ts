import { defineConfig, devices } from "@playwright/test";

/**
 * End-to-end: the real API (Development, own database `perfumeshop_e2e` on the docker-compose Postgres),
 * a mock Fragella server and the Vite dev server. Needs `docker compose up -d postgres` and the .NET SDK.
 */
const runId = (process.env.E2E_RUN_ID ??= Date.now().toString(36));
const apiPort = 5180;
const webPort = 5174;
const fragellaPort = 4010;

export const e2eAdmin = { email: "e2e-admin@perfumeshop.test", password: "E2e-Admin-Password-1!" };
export const e2eApi = `http://127.0.0.1:${apiPort}`;
/** The Development console SMS sender appends every SMS here; the tests read OTP codes from it. */
export const smsFile = `${process.cwd()}/test-results/e2e-sms-${runId}.jsonl`;

export default defineConfig({
  testDir: "./e2e",
  timeout: 180_000,
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL: `http://localhost:${webPort}`,
    locale: "sq-XK",
    timezoneId: "Europe/Belgrade",
    trace: "retain-on-failure",
  },
  projects: [
    { name: "desktop", use: { ...devices["Desktop Chrome"] } },
    { name: "phone", use: { ...devices["Pixel 7"] } },
  ],
  webServer: [
    {
      command: "node e2e/fragella-mock.mjs",
      url: `http://127.0.0.1:${fragellaPort}/health`,
      env: { FRAGELLA_MOCK_PORT: String(fragellaPort), E2E_RUN_ID: runId },
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
          process.env.E2E_DATABASE ?? "Host=localhost;Port=5432;Database=perfumeshop_e2e;Username=perfumeshop;Password=perfumeshop",
        Auth__SeedAdmin__Email: e2eAdmin.email,
        Auth__SeedAdmin__Password: e2eAdmin.password,
        Fragella__BaseUrl: `http://127.0.0.1:${fragellaPort}/api/v1/`,
        Fragella__ApiKey: "e2e-fragella-key",
        // Real background jobs (outbox → payments, receipts, SMS), polled every second so the flow takes seconds.
        BackgroundJobs__Enabled: "true",
        BackgroundJobs__PollingIntervalSeconds: "1",
        Orders__OutboxCron: "*/2 * * * * *",
        Shipping__OutboxCron: "*/2 * * * * *",
        Payments__OutboxCron: "*/2 * * * * *",
        Notifications__DispatchCron: "*/5 * * * * *",
        Notifications__DevelopmentSmsFile: smsFile,
        Customers__Otp__MaxCodesPerIp: "1000",
        Catalog__Images__LocalPath: "../../../web/apps/admin/test-results/e2e-media",
      },
    },
    {
      command: `pnpm exec vite --port ${webPort} --strictPort`,
      url: `http://localhost:${webPort}`,
      reuseExistingServer: false,
      env: { API_PROXY_TARGET: `http://127.0.0.1:${apiPort}` },
    },
  ],
});
