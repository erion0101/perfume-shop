# Before production

Everything that is known to be missing or development-only. Nothing here blocks local development;
all of it must be done (or consciously accepted) before real customers use the shop.
Update this list whenever something is added or finished.

## Security

| # | Item | Why | Notes |
|---|---|---|---|
| 1 | **Forwarded headers behind the Coolify proxy (trusted proxy only)** | Behind Traefik/Coolify every request comes from the proxy's IP. The per-IP OTP limit (10 codes / 15 min) would then hit all customers together, and logs/refresh tokens record the wrong IP. | `UseForwardedHeaders` with `ForwardedFor` + `ForwardedProto`, and `KnownProxies`/`KnownNetworks` set to the proxy's address only (config, e.g. `ForwardedHeaders:KnownProxies`). Never trust `X-Forwarded-For` from anyone else, or attackers bypass the IP limit. Add a test that a spoofed header from an untrusted IP is ignored. |
| 2 | **2FA for Admin/Staff** | Staff accounts can change prices, block customers, adjust stock. Password + lockout only today. | ASP.NET Identity TOTP (authenticator app). Make it mandatory for Admin. |
| 3 | **Rate limit staff login per IP** | Lockout is per account (5 failures / 15 min); one IP can still try many accounts. | ASP.NET Core rate limiter on `/api/auth/staff/login` (after #1, so the real IP is used). |
| 4 | **Production secrets** | Outside Development the API refuses to start without them. | Env vars in Coolify: `Auth__Jwt__SigningKey`, `Customers__Otp__HashKey` (both ≥ 32 bytes, random), `Fragella__ApiKey`. Back them up in a password manager. Rotating the JWT key signs everyone out. |
| 5 | **Seed admin** | `Auth__SeedAdmin__Email/Password` create the first admin on startup. | Use a strong password, sign in, then remove the password env var (an existing account is never overwritten). |
| 6 | **Blocked customers keep their access token up to 15 min** | Access tokens are not checked against the DB on every request. Their own account endpoints already return 403; other endpoints (e.g. checkout later) must check `ICustomersApi` too. | Accept, or shorten `Auth:Jwt:AccessTokenMinutes`, or check the security stamp. |
| 7 | **HTTPS, HSTS, CORS** | Not configured yet. | TLS at the proxy; `UseHsts`; CORS for the storefront and admin origins only. |
| 8 | **Swagger/OpenAPI** | Only mapped in Development (tested). | Keep it that way, or protect it if needed in staging. |

## Integrations

| # | Item | Notes |
|---|---|---|
| 9 | **SMS provider** | See #45-#47. Outside Development `UnconfiguredSmsSender` is used: OTP requests return 503 and every notification SMS fails (retried, then `Failed`) until a real provider is implemented. |
| 10 | **Fiscal receipts: real provider** | Implemented behind `IFiscalReceiptService`, but only with the NON-FISCAL development provider. Outside Development every receipt fails (see #36). Required by law for every sale. |
| 11 | **Fragella mapping verified with real data** | Mapping is tested against a hand-written sample in the documented format. Check a few real imports (`catalog.fragella_fragrances.raw`) before importing the full range. |
| 12 | **Meilisearch, R2 images** | In the tech stack, not wired yet. Meilisearch needs a production master key. Email: see #48. |

## Background jobs (Hangfire)

Hangfire runs in the API (PostgreSQL storage, schema `hangfire`). Running: `orders-outbox`, `shipping-outbox`,
`payments-outbox`, `notifications-dispatch` (every 30 s), `orders-expire-pending` (every 5 min),
`notifications-low-stock` (08:00) and `notifications-unpaid-cod` (08:05), both in Europe/Belgrade time all year. A job that ends Failed
emails staff (`staff-job-failed`). Still to add:

| # | Job | Notes |
|---|---|---|
| 13 | **Delete old OTP codes** | `customers.phone_verifications` older than 30 days. |
| 14 | **Delete expired / revoked refresh tokens** | `identity.refresh_tokens` expired, or revoked more than 30 days ago (keep recent ones for reuse detection). |
| 15 | **Release expired stock reservations** | Expired `inventory.stock_reservations` are released lazily when the same variant is reserved again, and expired orders release theirs through `orders-expire-pending`. A sweep (`StockReservationSweeper`) still needs a recurring job for orphaned holds (order save failed after reserving). |
| 16 | **Clean the Fragella search cache** | Delete `catalog.fragella_search_cache` rows past `expires_at_utc`. |
| 17 | ~~Outbox: inbox / dead letters~~ | **Done:** handlers run once per event id (`<schema>.inbox_messages`). When an outbox message fails for the 10th time (a handler keeps throwing), staff get one email per message (`staff-dead-letter`, uses the job-failure switch). The row stays with its error; after fixing the cause set `attempts = 0` to resend. |
| 17a | **Hangfire dashboard** | Mapped only in Development (local requests). If needed in production, protect it with an Admin login (cookie) — JWT does not work for the dashboard. |

## Orders

| # | Item | Notes |
|---|---|---|
| 25 | **Delivery fees and anti-fake-order limits** | Seeded values are placeholders: Kosovo 2.00 € (free from 40.00 €), 2 open COD orders per customer, 2 refused parcels, 48 h to confirm. Set real values in the Admin API. |
| 26 | **Order ↔ stock consistency** | Orders and Inventory are separate transactions. Every step is idempotent and retried safely, but if saving an order fails after Inventory already changed (e.g. a crash between the two), the state can differ until the step is repeated. Add a daily reconciliation report (orders vs. reservations). |
| 27 | **Customers blocked by refusals** | Orders refuses new COD orders above the refusal limit, but does not block the customer in Customers. Decide whether `OrderRefused` should block automatically. |
| 28 | ~~Order emails/SMS~~ | **Done** in Notifications (customer SMS; customer emails are not planned for v1). |
| 29 | ~~Fiscal receipt at shipment~~ | **Done** in Payments: sale receipt on `OrderShipped`, storno on `OrderRefused`. The real provider is #36. |

## Shipping

| # | Item | Notes |
|---|---|---|
| 30 | **Shop contact on packing slips** | `Shipping:Shop` (name, phone, address) is a placeholder (`+383 44 000 000`). Set the real phone: customers call it from the slip. |
| 31 | **Real courier terms** | Fees, COD commission and remittance days are entered per courier by Admin; check them against each contract. Only couriers with "accepts flammable goods" can be used (UN1266). |
| 32 | **Courier file formats** | `ManualCourierAdapter` reads `tracking_number;status;note` and `tracking_number;collected_amount;fee` (with Albanian aliases). Ask each courier for a real export and add column aliases or a dedicated adapter if theirs differs. |
| 33 | **Statement amounts that differ** | Lines with a wrong amount are reported, not booked. Staff check with the courier and book the cash with `POST /api/admin/shipping/shipments/{id}/cash-collected` (note required when it differs). |
| 34 | ~~Old Orders ship/delivered/refused endpoints~~ | **Done:** removed. Every shipping transition goes through Shipping (`IOrdersApi`); statement mismatches are booked with `POST /api/admin/shipping/shipments/{id}/cash-collected`. |
| 35 | ~~Overdue COD alert~~ | **Done:** daily `notifications-unpaid-cod` emails staff (`staff-unpaid-deliveries`). |

## Payments and fiscal receipts

| # | Item | Notes |
|---|---|---|
| 36 | **Choose the real fiscal provider together with the accountant** | Only `DevelopmentFiscalReceiptService` exists (stores receipts in the DB, internal number, printed "JO FISKAL – NON-FISCAL"). It is registered only in Development (tested). Everywhere else `UnconfiguredFiscalReceiptService` makes every issue fail loudly: receipt `Failed`, error log, counted in the daily summary. Implement the chosen provider as a new `IFiscalReceiptService` and register it outside Development. Answer the open questions below first. |
| 37 | **Open question 1: device or software?** | A certified fiscal cash register / fiscal printer in the warehouse, or certified fiscal software / an online fiscalisation service? Who supplies and certifies it (ATK list)? |
| 38 | **Open question 2: is there an API?** | Can our server send receipts to it (HTTP API, SDK, local driver, file drop), or is it manual (staff types the receipt)? What does it return (fiscal number, QR code, signature) that we must store and print? Test environment? |
| 39 | **Open question 3: when is the receipt issued?** | We issue it at shipment (the receipt travels in the parcel; the customer pays the courier on delivery). Confirm with the accountant that shipment (not delivery or cash collection) is the right moment for COD, and what happens with the payment method on the receipt (cash, collected by the courier). |
| 40 | **Open question 4: storno process** | Refused parcels get a storno linked to the original sale when the parcel is back in the warehouse. Confirm: full storno vs. credit note, time limits, whether the original receipt must be returned/attached, and how partial returns would work later. |
| 41 | **Open question 5: courier fees in the books** | Couriers keep their COD fee and pay us the net (`courier_fee`, `net_received` per payment). Does the accountant book gross sale + fee as an expense (courier invoice), or net? Do couriers issue an invoice per statement? Which reports does the accountant need (daily summary, CSV export)? |
| 42 | **Business details on receipts** | `Payments:Business` (name, NUI/fiscal number `000000000`, address, phone) are placeholders. Set the real ones (env vars `Payments__Business__...`). |
| 43 | **Failed receipts** | Staff get an email per failed attempt (`staff-receipt-failed`), the manifest flags the parcel, and handover needs an override. Retry is still by hand (`POST .../receipts/{id}/retry`): consider an automatic retry job once the real provider is known. |
| 44 | **Receipt numbering** | Our own internal numbers `PS-R-<year>-<000001>` (gap-free per year). Check whether the fiscal provider needs its own numbering only, and whether ours may appear on the printed receipt. |

| 53 | **Handover without receipt (override)** | Allowed with a reason, stored on the shipment (`receipt_override_*`), in the timeline and as a warning log. Agree with the accountant how such parcels are fiscalised afterwards, and review overrides regularly. |

## Notifications

| # | Item | Notes |
|---|---|---|
| 45 | **Choose the SMS provider** | Needs delivery to Kosovo (Vala/IPKO, +383), later Albania and North Macedonia, an alphanumeric sender ID, delivery reports and an HTTP API. Candidates to compare on price per segment and Kosovo delivery: Infobip, Twilio, Vonage, a local aggregator. Implement it as a new `ISmsSender` (set `Name`, return the raw response) registered outside Development; OTP and notifications both use it. Keys in env vars only. |
| 46 | **Register the SMS sender name in Kosovo** | Alphanumeric sender IDs (e.g. "PerfumeShop", max 11 characters) usually must be registered with the operators through the provider (company documents, sometimes a letter of authorisation; can take days or weeks). Unregistered senders are replaced by a number or blocked. Set `Notifications:ShopName` to the registered name (it starts every SMS). |
| 47 | **SMS cost and content** | Customer templates are one GSM-7 segment with transliteration (ë→e, ç→c). Check the template list (`segments`, `warning`) after every edit. Confirm with the provider how they count segments and whether marketing rules apply (these are transactional only). |
| 48 | **Brevo setup (email)** | Create the Brevo account, verify the sending domain (SPF, DKIM, DMARC records in DNS), create a transactional API key (env var), and implement `IEmailSender` with Brevo's transactional email API. Until then staff alert emails fail outside Development (retried, then `Failed` in the log). |
| 49 | **Staff alert recipients** | Seeded empty: set them with `PUT /api/admin/notifications/settings/staff-alerts`. Without recipients alerts are only logged as warnings. |
| 50 | **Shop phone and quiet hours** | `Notifications:ShopPhone` (`+383 44 000 000`) is a placeholder and appears in SMS. Quiet hours 22:00-08:00 Europe/Belgrade; daily alert jobs run at 08:00/08:05 Europe/Belgrade (summer and winter). |
| 51 | **Notification log retention** | `notifications.messages` stores phone numbers, emails and message text (personal data). Decide a retention period (e.g. 12 months) and add a cleanup job; include it in the privacy policy (#23). |
| 52 | **Delivery reports** | "Sent" means the provider accepted the message. Add the provider's delivery-report webhook later if needed (store it on the attempt). |

## Admin panel

| # | Item | Notes |
|---|---|---|
| 54 | **Product photos: Cloudflare R2 + resizing** | Uploads (`POST /api/admin/catalog/images`) are stored on the API's disk and served from `/media/catalog/...` in Development only; elsewhere they return 503. Implement `IImageStorage` for R2 (public bucket/CDN URL, WebP/AVIF variants) and register it outside Development. Development URLs saved on variants point to the dev host. |
| 55 | **Admin and API on the same site** | The refresh cookie is `SameSite=Strict`, `Secure`, path `/api/auth/staff/session`. Serve the admin under the same site as the API (e.g. `admin.example.com` + `api.example.com`, or `/admin` and `/api` behind one proxy) or the browser will not send it. CORS for the admin origin only (#7), with credentials. |
| 56 | **Admin hosting headers** | Static build (`web/apps/admin/dist`). Add a strict Content-Security-Policy (no inline scripts), `X-Frame-Options: DENY`, `Referrer-Policy: same-origin`; `noindex` is already in `index.html`. |
| 57 | **Staff accounts** | Only the seeded admin exists. Add staff (role Staff) before going live; an account management page comes with admin part 3. |
| 59 | **New-order alerts in the admin** | The top bar polls Pending orders every 30 s (toast + optional sound). Fine for a few staff; switch to server-sent events if many staff keep the panel open. Sound needs one click on the page first (browser rule). |
| 60 | **Problem details extensions** | Domain errors can carry details (e.g. `parcels` for a missing receipt). Never put personal data or secrets there: they go to every client that triggers the error. |
| 58 | **E2E in CI** | The `e2e` job uses a Postgres service and the mock Fragella server; it never calls the real Fragella API. Keep it that way (quota). |

## Operations

| # | Item | Notes |
|---|---|---|
| 18 | **Migrations in deployment** | `Database:MigrateOnStartup` is true only in Development. Run migrations as a deploy step (EF migration bundle or `dotnet ef database update`) before the new version starts. |
| 19 | **Database backups** | Daily PostgreSQL backups off the VPS (Hetzner Storage Box / R2) + a restore test. |
| 20 | **Observability** | Seq, Sentry and OpenTelemetry are in the stack but not wired. Seq in docker-compose runs without authentication: dev only. |
| 21 | **Production compose / Coolify config** | docker-compose.yml is for local development (ports on 127.0.0.1, dev passwords). |
| 22 | **CI on GitHub** | The workflow has not run yet (repo not on GitHub). Tests need Docker (Testcontainers); GitHub's Ubuntu runners have it. |

## Legal / privacy

| # | Item | Notes |
|---|---|---|
| 23 | **Privacy policy and data requests** | We store phone numbers, names, addresses and IPs. Kosovo's data protection law (and GDPR for EU later): privacy notice, retention periods, deletion on request. |
| 24 | **Terms of sale, returns, COD rules** | Including what happens after refused parcels (customers can be blocked). |
