# Project Brief: Arabian Perfume E-commerce (Kosovo)

## Context
- Owner: Erion, .NET + React/TypeScript engineer, based in Lipjan, Kosovo.
- Goal: a production-grade, scalable e-commerce store selling **only Arabian / Middle Eastern perfumes** (Lattafa, Armaf, Afnan, Rasasi, Maison Alhambra, Al Haramain, Ajmal, French Avenue, Swiss Arabian, etc.).
- Markets: Kosovo first, then Albania and North Macedonia, EU later.
- Language: storefront in **Albanian first**, later Macedonian and English. Code, comments and identifiers in English.

## Business decisions (already made)
- Differentiate on **trust, curation and discovery**, not lowest price (competitors: GjirafaMall, Kaan Parfumes, Fragravia).
- Key features that set us apart: wide Arabian brand range, "smells like / similar profile" recommendations, note pyramid on every product, discovery/gift sets, attar/oud/bakhoor, authenticity info (batch codes, invoices).
- **Payments:** **Cash on Delivery (COD) ONLY.** The customer pays the courier when the order arrives. **No card payments, no payment gateway** in this version. Keep an `IPaymentProvider` abstraction with a single `CashOnDeliveryProvider` so a card gateway can be added later without refactoring.
- **Shipping:** perfume is a dangerous good (UN1266, Class 3). Road couriers only. Local couriers have no public APIs, so start with manual/CSV and abstract behind an adapter.
- **Product data:** use the **Fragella API** (Basic plan, 5,000 requests/month). Never scrape Fragrantica. Fetch once on import and store in our own database; never call the API on page views. Photos come from the distributor, not from the API.
- Every online sale in Kosovo needs a fiscal receipt. Prices include 18% VAT.

## Tech stack
| Part | Choice |
|---|---|
| Backend | ASP.NET Core (.NET 10), **modular monolith** |
| ORM / DB | EF Core + PostgreSQL (one database, **one schema per module**); `jsonb` for notes/accords; `pgvector` later for similarity |
| Storefront | Next.js (App Router) + TypeScript + Tailwind + shadcn/ui, SSR/ISR for SEO |
| Admin panel | React + Vite + TypeScript + TanStack Query + shadcn/ui |
| Search | Meilisearch (filters by brand, note, gender, family, price; typo tolerant) |
| Background jobs | Hangfire (Postgres storage): Fragella imports, emails/SMS, outbox, COD reconciliation |
| Auth | ASP.NET Identity + phone OTP (reduce fake COD orders) |
| Images | Cloudflare R2 + resizing (WebP/AVIF) |
| Email | Brevo or Resend |
| Hosting | Hetzner VPS + Docker + Coolify |
| CI/CD | GitHub Actions |
| Observability | Sentry + Seq, OpenTelemetry |
| Not now | Redis (add only when needed), microservices (no) |

## Architecture rules (modular monolith)
1. **One deployable app** (`PerfumeShop.Api`) that loads all modules via an `IModule` interface (`Register(services, config)` + `MapEndpoints(app)`).
2. Each module = **two projects**: `PerfumeShop.Modules.X` (internal implementation) and `PerfumeShop.Modules.X.Contracts` (public interfaces, DTOs, integration events).
3. Modules may reference only `Shared.*` and **other modules' `.Contracts`**, never another module's internals. Everything is `internal` by default.
4. Each module has its **own DbContext, own schema, own migrations**. No foreign keys or joins across schemas. Orders store a snapshot of name/price at purchase time.
5. Communication: **sync** via contract interfaces (e.g. `ICatalogApi.GetVariantPriceAsync`), **async** via integration events (e.g. `OrderPlacedIntegrationEvent`). Start with an in-process event bus + **outbox table** processed by Hangfire.
6. Inside modules use **vertical slices** (`Features/<UseCase>/` with Endpoint, Command/Query, Handler, Validator). No full Clean Architecture per module.
7. A **rich domain model** only where logic is complex: `Order` aggregate (`Confirm()`, `Ship()`, `MarkDelivered()`, `MarkCashCollected()`, `Cancel()`), Payments, Shipping.
8. Abstractions: `IPaymentProvider` (COD now, ProCredit/Raiffeisen next, Stripe later) and `ICourierAdapter` (+ COD reconciliation module).
9. Architecture tests with **NetArchTest** enforce the boundaries in CI.

## Solution structure
```
PerfumeShop/
├── PerfumeShop.sln
├── docker-compose.yml                 # Postgres, Meilisearch, Seq
├── Directory.Build.props              # net10.0, nullable, warnings as errors
├── src/
│   ├── Bootstrapper/PerfumeShop.Api/
│   ├── Shared/
│   │   ├── PerfumeShop.Shared.Kernel/          # Entity, Result, Money, IDomainEvent
│   │   └── PerfumeShop.Shared.Infrastructure/  # IModule, event bus, outbox, EF helpers
│   └── Modules/
│       ├── Catalog/        (+ .Contracts)
│       ├── Inventory/      (+ .Contracts)
│       ├── Orders/         (+ .Contracts)
│       ├── Payments/       (+ .Contracts)
│       ├── Shipping/       (+ .Contracts)
│       ├── Customers/      (+ .Contracts)
│       └── Notifications/  (+ .Contracts)
├── web/                   # pnpm workspaces + Turborepo
│   ├── apps/
│   │   ├── storefront/    # Next.js (customers)
│   │   └── admin/         # React + Vite (admin/staff)
│   └── packages/
│       ├── ui/            # shared components (Tailwind + shadcn/ui)
│       ├── api-client/    # TypeScript client generated from the .NET OpenAPI
│       └── config/        # shared tsconfig, eslint, tailwind presets
└── tests/
    ├── PerfumeShop.ArchitectureTests/
    └── PerfumeShop.Modules.<X>.Tests/
```

Module internal layout:
```
PerfumeShop.Modules.Catalog/
├── Domain/          # Fragrance, FragranceVariant, Brand, Note, Accord (no EF/HTTP)
├── Features/        # CreateFragrance/, ImportFromFragella/, GetFragranceBySlug/, SearchFragrances/
├── Infrastructure/  # CatalogDbContext (schema "catalog"), configurations, FragellaClient
└── CatalogModule.cs
```

## Catalog data model (perfume-specific)
- **Fragrance**: brand, name, slug, concentration (EDP/EDT/Extrait/Attar), gender, olfactive family, year, perfumer(s), description (Albanian), notes pyramid (top/heart/base), main accords with percentages, longevity, sillage, seasons, occasions, Fragella id + raw JSON snapshot, "similar to" references.
- **FragranceVariant** (what is sold): size in ml, type (full bottle / tester / mini / gift set / sample set), EAN, SKU, price (EUR, VAT incl.), compare-at price, stock, images, batch code tracking.
- **Brand**: name, slug, country, logo, description.

## Fragella integration
- Base URL `https://api.fragella.com/api/v1/`, header `x-api-key`.
- Endpoints: `GET /fragrances?search=`, `GET /brands/{brand}`, `GET /fragrances/similar`, `GET /fragrances/match`.
- Admin flow: search by name → pick result → import (notes, accords, longevity, sillage, gender, type) → admin adds price, stock, ml, EAN, photos, Albanian description → save.
- Cache/store everything locally; respect the plan limit; store the raw response for re-processing.

## MVP scope (v1)
**Storefront:** home, brand pages, product page with note pyramid and accords, filters (brand, gender, note, family, price), search, cart, COD checkout with phone OTP, order confirmation, Albanian SEO (Schema.org Product/Offer/BreadcrumbList).
**Admin:** Fragella import, product/variant management, stock, orders with status flow New → Confirmed → Shipped → Delivered → CashCollected (+ Cancelled/Refused), basic dashboard.

**Later (v2+):** scent quiz, "smells like" with pgvector, card payments, reviews with longevity/sillage votes, loyalty points, price/back-in-stock alerts, Albania + North Macedonia (EUR/ALL/MKD), English/Macedonian, AI scent finder on our own data.

## Current status (2026-11-26)

Backend v1 is complete: every module below is implemented, migrated, tested (Testcontainers + unit tests, ~570 tests)
and has an example file in `src/Bootstrapper/PerfumeShop.Api/http/`. Docs: `docs/database.md` (tables + decisions log)
and `docs/before-production.md` (everything dev-only or open; keep it up to date).

| Module | Status | Owns |
|---|---|---|
| Administration | done | business details (receipts, packing slips, SMS; seeded from config), audit log (`IAuditLog`, `.Audited(...)` endpoint filter) |
| Shared | done | `IModule`, Result/Error → ProblemDetails, FluentValidation filter, JWT policies (Admin / StaffOrAdmin / Customer), per-module outbox (Hangfire, `FOR UPDATE SKIP LOCKED`, 10 attempts) + inbox, dead-letter and job-failure hooks |
| Catalog | done | brands, fragrances (notes, accords, Albanian text), variants, Fragella import (budget, 30-day search cache, raw JSON stored) |
| Customers | done | customers, addresses, ASP.NET Identity (schema `identity`), phone OTP, JWT + refresh-token rotation, seeded admin from config |
| Inventory | done | stock items, batches (batch code + supplier invoice), FIFO reservations/commits, append-only movements, low-stock thresholds |
| Orders | done | cart quote, COD order aggregate (Pending → Confirmed → Shipped → Delivered → CashCollected, + Cancelled/Expired/Refused), anti-fake-order limits, delivery fees, 48 h expiry |
| Shipping | done | couriers (must accept UN1266), shipments, tracking numbers, manifests, courier CSV files, COD settlement statements, reports |
| Payments | done | one COD payment per order, fiscal receipts (sale at shipment, storno on refusal) behind `IFiscalReceiptService` |
| Notifications | done | customer SMS + staff alert emails, Albanian templates in the DB, notification log, quiet hours |
| Admin panel (web/apps/admin) | done | part 1: foundation (login, layout, roles, i18n `sq.ts`, generated client), Catalog, Inventory. Part 2: Orders, Shipping, Payments. Part 3: Customers (list, detail, block/unblock with reason), Staff accounts (create with temporary password, deactivate, reset, last login), My account (password, TOTP 2FA + recovery codes, required for Admin), Audit log, Notifications (templates with segment counts, editor with live preview, log with attempts + resend, staff alert settings), Settings (business details + placeholder banner, delivery fees, anti-fake-order rules, Fragella usage), Dashboard (today, money, alerts, 14-day chart, top sellers, refusal rate) |
| Storefront (web/apps/storefront) | not started | |

### Decisions made so far (binding)
- **NEVER create or change anything in `web/apps/storefront`** (not even after the admin is finished, not for
  shared refactors). The storefront will be designed separately later. Shared packages (`web/packages/*`) may change
  only in ways that keep the storefront building untouched.
- **Cash on Delivery only.** No card payments, no gateway. `IPaymentProvider` has only `CashOnDeliveryProvider`.
- **Shipping owns couriers, tracking and COD settlement.** Every shipping transition of an order (Shipped, Delivered,
  Refused, CashCollected) goes through Shipping, which calls `IOrdersApi`; Orders has no ship/deliver endpoints and never
  references Shipping. Stock returns to its batches only when a refused parcel is back in the warehouse.
- **Payments owns payments and fiscal receipts.** It only listens to Orders/Shipping events (inbox, once per event).
  Sale receipt when the order ships, storno linked to it when a refused parcel is back. Only a NON-FISCAL development
  provider exists; outside Development every receipt fails loudly until the real provider is chosen with the
  accountant. A failed receipt never blocks creating a shipment, but a parcel cannot be handed to the courier without
  an issued receipt unless staff override with a reason (stored + logged); the manifest flags such parcels.
- **Notifications rules.** Customer SMS (Albanian, transliterated ë→e / ç→c to stay one GSM-7 segment) for order
  placed, confirmed, parcel handed over to the courier, delivered, cancelled, expired; never for refused parcels.
  Quiet hours 22:00-08:00 Europe/Belgrade for customer SMS (queued until 08:00); OTP codes are exempt and never stored.
  Staff alert emails: new order, failed fiscal receipt, low stock and unpaid deliveries (daily 08:00 Kosovo time),
  failed background jobs and dead-lettered events. Every message is logged with attempts; retries 1 min/5 min/30 min/2 h,
  then Failed (staff can resend). Each event produces each message at most once (unique dedup key).
- **Dev-only things never run outside Development** (tested): console SMS/email senders, ephemeral JWT/OTP keys,
  development fiscal provider, Swagger/OpenAPI, Hangfire dashboard. Secrets only in user-secrets / env vars.
- Stock lives in Inventory, not Catalog. Identity tables live in Customers (schema `identity`). Bakhoor/non-liquid sizes
  are `size_value` + `size_unit` (Ml, Gram, Piece). Prices include 18 % VAT; VAT is rounded per line.
- Hangfire recurring jobs run in UTC unless a time zone is given (daily alerts use Europe/Belgrade).
- **Frontend contract:** `@perfumeshop/api-client` is generated from the committed `openapi.json` (backend test
  `OpenApiSnapshotTests` keeps it current); no hand-written API types. Endpoints return typed results so every response
  has a schema; JSON numbers are strict (no numbers as strings).
- **Admin auth:** access token in memory only; refresh token in an HttpOnly, Secure, SameSite=Strict cookie
  (`/api/auth/staff/session`, rotated, reuse revokes the session), plus the `X-PerfumeShop-Client` header against CSRF.
  Admin: everything; Staff: orders, shipping, payments, inventory, customers, notifications (read-only templates/alerts)
  and read-only catalog lists (to pick variants). Admin tokens need the `mfa` claim (2FA) unless
  `Auth:Staff:RequireTwoFactorForAdmins` is false (only the e2e run sets that).
- **Audit:** every state-changing admin endpoint records who did what (`IAuditLog`, Administration module); prices with
  before → after. Business details live in `administration.business_profile`, never hard-coded.
- **Admin UI texts** are Albanian and live only in `web/apps/admin/src/i18n/sq.ts` (API error codes → messages too).
  Money and dates are formatted by hand (decimal comma, Europe/Belgrade), never with browser locale data.

## First tasks for Claude Code (all done; kept for history)
1. Scaffold the solution and projects as above (`net10.0`), `Directory.Build.props`, `docker-compose.yml` (Postgres, Meilisearch, Seq).
2. Implement `Shared.Kernel` (Entity, Result, Money, IDomainEvent) and `Shared.Infrastructure` (IModule, in-process event bus, outbox).
3. Build the **Catalog** module: domain, `CatalogDbContext` with schema `catalog`, migrations, endpoints, and `ImportFromFragella` feature.
4. Add architecture tests (NetArchTest) for module boundaries.
5. Then Orders (COD), Customers (phone OTP), Next.js storefront, React admin.
