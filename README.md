# PerfumeShop

E-commerce store for Arabian / Middle Eastern perfumes (Kosovo first).
ASP.NET Core (.NET 10) modular monolith + Next.js storefront + React admin.
See [CLAUDE.md](CLAUDE.md) for the full project brief and architecture rules.

> **Payments:** Cash on Delivery only. There is no card payment or payment gateway in v1.

## Prerequisites

- .NET SDK 10 (`global.json` pins `10.0.x`)
- Node.js 22+ and pnpm (`corepack enable` picks the version from `web/package.json`)
- Docker (for Postgres, Meilisearch, Seq)

## 1. Infrastructure (Docker)

```bash
cp .env.example .env        # optional, defaults work out of the box
docker compose up -d
```

| Service     | URL                         | Notes                                   |
|-------------|-----------------------------|-----------------------------------------|
| PostgreSQL  | `localhost:5432`            | db/user/password: `perfumeshop` (pgvector included) |
| Meilisearch | http://localhost:7700       | master key in `.env` / compose default  |
| Seq (logs)  | http://localhost:8341       | ingestion on `localhost:5341`           |

Stop with `docker compose down` (add `-v` to delete the data volumes).

## 2. API

```bash
dotnet build PerfumeShop.sln
dotnet run --project src/Bootstrapper/PerfumeShop.Api
```

- Swagger UI: http://localhost:5080/swagger
- OpenAPI document: http://localhost:5080/openapi/v1.json
- Health: http://localhost:5080/health

In Development the API applies migrations on startup (`Database:MigrateOnStartup`).

### Secrets (never in appsettings)

Set them once with user-secrets (Development) or environment variables (production, `__` instead of `:`):

```bash
cd src/Bootstrapper/PerfumeShop.Api
dotnet user-secrets set "Auth:Jwt:SigningKey"    "$(openssl rand -base64 48)"   # signs access tokens
dotnet user-secrets set "Customers:Otp:HashKey"  "$(openssl rand -base64 48)"   # hashes SMS codes
dotnet user-secrets set "Auth:SeedAdmin:Email"    "you@example.com"             # first Admin account
dotnet user-secrets set "Auth:SeedAdmin:Password" "<12+ chars, upper, lower, digit, symbol>"
dotnet user-secrets set "Fragella:ApiKey"         "<your key>"
```

- Without the two keys, **Development** generates random ones per run (a warning is logged; tokens stop
  working after a restart). Every other environment **refuses to start** without them.
- The admin account is created on startup if it does not exist (its password is never overwritten).
- In Development, SMS codes are written to the API console (`DEV SMS to +383…`). Outside Development no
  SMS provider exists yet, so code requests return 503 until one is added.

Background jobs run with Hangfire (dashboard in Development: http://localhost:5080/hangfire).

Sign in: customers with `POST /api/auth/otp/request` + `/api/auth/otp/verify`, admin/staff with
`POST /api/auth/staff/login`. Send the access token as `Authorization: Bearer <token>`.

Fragella API key (a secret: never put it in appsettings):

```bash
# local development (stored outside the repo, Development environment only)
dotnet user-secrets set "Fragella:ApiKey" "<your key>" --project src/Bootstrapper/PerfumeShop.Api
# production: environment variable
export Fragella__ApiKey="<your key>"
```

`Fragella:MonthlyRequestLimit` (default 4500) caps real API calls per month; searches are cached
for 30 days. Example requests for every endpoint: `src/Bootstrapper/PerfumeShop.Api/http/Catalog.http` and
`Customers.http`, `Inventory.http`, `Orders.http`, `Shipping.http` (couriers, manifests, courier CSV files, COD settlement; sample CSVs in `http/samples/`) `Payments.http` (payments, NON-FISCAL dev receipts, daily cash summary) and `Notifications.http` (templates, SMS segments, notification log, staff alerts) (each starts with a sign-in request and reuses its token).
Known gaps before going live: [docs/before-production.md](docs/before-production.md).

Tests (including the module boundary architecture tests and Testcontainers integration tests,
so Docker must be running):

```bash
dotnet test --solution PerfumeShop.sln
```

Adding an EF Core migration for a module (example: Catalog):

```bash
dotnet tool restore                      # once; dotnet-ef is pinned in dotnet-tools.json
dotnet ef migrations add <Name> \
  --project src/Modules/Catalog/PerfumeShop.Modules.Catalog \
  --startup-project src/Bootstrapper/PerfumeShop.Api \
  --context CatalogDbContext \
  --output-dir Infrastructure/Migrations

# apply to the local Postgres from docker compose
dotnet ef database update \
  --project src/Modules/Catalog/PerfumeShop.Modules.Catalog \
  --startup-project src/Bootstrapper/PerfumeShop.Api \
  --context CatalogDbContext
```

## 3. Web apps

```bash
cd web
pnpm install
pnpm dev          # storefront + admin together (Turborepo)
```

| App        | URL                    | Run alone                                  |
|------------|------------------------|--------------------------------------------|
| Storefront | http://localhost:3000  | `pnpm --filter @perfumeshop/storefront dev` |
| Admin      | http://localhost:5173  | `pnpm --filter @perfumeshop/admin dev`      |

Other commands: `pnpm build`, `pnpm lint`, `pnpm typecheck`, `pnpm test` (Vitest), `pnpm storybook`
(shared components on http://localhost:6006), `pnpm e2e` (Playwright, see below).

### Admin panel (web/apps/admin)

Albanian UI for staff; all texts are in `src/i18n/sq.ts`. Sign in with the seeded admin
(`Auth:SeedAdmin` in user-secrets). The access token lives only in memory; the refresh token is an HttpOnly
cookie set by `POST /api/auth/staff/session` (path `/api/auth/staff/session`, SameSite=Strict, rotated on every
refresh). Admin sees Catalog + Inventory; Staff sees Inventory (and may read the catalog to pick variants).
In development Vite proxies `/api` and `/media` (uploaded photos) to http://localhost:5080.

### Typed API client (web/packages/api-client)

Generated from the API's OpenAPI document; no hand-written API types. The document is committed as
`web/packages/api-client/openapi.json`, and a backend test fails when it is out of date. After changing endpoints:

```bash
UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --project tests/PerfumeShop.Modules.Catalog.Tests --filter-class "*OpenApiSnapshotTests"
cd web && pnpm generate-api           # openapi.json → src/schema.d.ts
# or, with the API running in Development: pnpm --filter @perfumeshop/api-client regenerate
```

### End-to-end tests

`pnpm e2e` starts a mock Fragella server, the real API (Development, database `perfumeshop_e2e` on the
docker-compose Postgres, its own seeded admin, Hangfire polling every second) and the admin app, then runs on a
desktop and a phone viewport:
- catalog: login → import a fragrance → add a variant with a photo → receive stock → publish;
- daily COD flow: order placed through the API (customer signs in with an OTP read from the Development SMS file)
  → confirm in the queue → create shipment → print packing slip and receipt → handover with the manifest →
  courier status CSV (delivered) → courier statement CSV → order CashCollected, payment Collected with fee and net;
- refused parcel: handed over → refused → back in the warehouse → order Refused, payment Failed, storno receipt. Needs `docker compose up -d
postgres` and, once, `pnpm --filter @perfumeshop/admin exec playwright install chromium`.

## Repository layout

```
src/Bootstrapper/PerfumeShop.Api   host: loads every module via IModule
src/Shared/                        Kernel (domain primitives) + Infrastructure (IModule, events, outbox, EF)
src/Modules/<Name>/                PerfumeShop.Modules.<Name> (internal) + .Contracts (public)
tests/                             architecture tests + per-module tests
web/apps/                          storefront (Next.js), admin (React + Vite)
web/packages/                      ui, api-client, config
```

Module rules: a module may reference only `Shared.*` and other modules' `.Contracts`.
Each module has its own DbContext and PostgreSQL schema. CI fails if a boundary is broken.
# perfume-shop
