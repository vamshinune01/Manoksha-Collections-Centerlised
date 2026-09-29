# Manoksha Collections — V1 monorepo

Multi-branch retail platform (imitation jewellery, sarees, kids dresses) for Karimnagar, Hyderabad and Mulugu.

| Source of truth | Document |
|---|---|
| Business rules | *Manoksha Collections V1 Final Implementation Requirements & Architecture Specification* (24 Sep 2026) |
| Owner clarifications | [docs/architecture/ADR-001-V1-Owner-Clarifications.md](docs/architecture/ADR-001-V1-Owner-Clarifications.md) |
| Technical design (approved) | [docs/architecture/V1-Architecture-and-Design.md](docs/architecture/V1-Architecture-and-Design.md) |

## Workspaces

| Folder | What | Stack |
|---|---|---|
| [`manoksha-backend/`](manoksha-backend) | The **one** central business backend (modular monolith: API + Worker) | .NET 8, EF Core, PostgreSQL |
| [`manoksha-admin-web/`](manoksha-admin-web) | Internal app for Owner, branch managers, employees (role-based) | Next.js 16, TypeScript, Tailwind 4 |
| [`manoksha-customer-web/`](manoksha-customer-web) | Public storefront (Phase 6) + separated `/reseller` area (live) | Next.js 16, TypeScript, Tailwind 4 |
| [`manoksha-mobile-pos/`](manoksha-mobile-pos) | Store POS, scanning, stock operations | Flutter (Phase 8) |
| [`packages/api-client/`](packages/api-client) | TypeScript types generated from the backend OpenAPI contract — no business logic | openapi-typescript |
| [`contracts/openapi/`](contracts/openapi) | Committed OpenAPI snapshot; CI fails if it drifts from the API | |
| [`infra/`](infra) | Local docker compose, database roles, (Phase 10) Terraform for GCP | |

All business rules and authorization live in the backend. Frontends only display backend-authoritative data.

## Local development

Prerequisites: .NET SDK 8, Node 20.9+, ffmpeg (video optimization), Google Cloud CLI signed in (`gcloud auth application-default login`).

Local development uses the **same cloud services as staging** — nothing runs locally except the apps:
- **Database:** Supabase PostgreSQL (shared with staging). The connection string lives only in .NET user-secrets on your machine.
- **Photos/videos/files:** Google Cloud Storage buckets `manokshacenterlised-private` / `manokshacenterlised-media`; the local API
  acts as the `manoksha-run` service account through your own Google login (no key files).

```bash
# 1. Once: store the Supabase (Session pooler) connection string for the API and the Worker — prompts for the password
read -rs "PW?Supabase DB password: " && echo && CS="Host=aws-0-ap-south-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.pixtvycazmggmgmaepoe;Password=$PW;SSL Mode=Require;Maximum Pool Size=10" && for p in Api Worker; do dotnet user-secrets set "ConnectionStrings:Manoksha" "$CS" --project "manoksha-backend/src/Hosts/Manoksha.$p"; done; unset PW CS

# 2. API on http://localhost:5080 (Swagger at /swagger) and the Worker (reservation sweeper, payment poller, outbox, housekeeping)
dotnet run --project manoksha-backend/src/Hosts/Manoksha.Api
dotnet run --project manoksha-backend/src/Hosts/Manoksha.Worker

# 3. Admin web on http://localhost:3001 and customer/reseller web on http://localhost:3002
npm install
cp manoksha-admin-web/.env.example manoksha-admin-web/.env.local
cp manoksha-customer-web/.env.example manoksha-customer-web/.env.local
npm run admin:dev
npm run customer:dev
```

Because the database is shared with staging, **do not run `seed-dev`** — create data through the admin screens. Development OTP codes
are printed in the API log (`[FAKE SMS]`). Development-only adapters (fake SMS, logging email, UPI payment simulator) are refused in
Production. `infra/docker-compose.local.yml` (local Postgres/Mailpit) is no longer used for development; the automated tests start
their own throwaway PostgreSQL container and never touch Supabase.

**Online payments in development.** Until the Owner selects a UPI gateway, `Integrations:Payments:Provider=Simulator` is used:
checkout redirects to a simulated UPI app at `http://localhost:3002/pay/simulator/…` where you can pay, decline, "lose" the
webhook or pay a different amount.

A real environment's first Owner is created with:

```bash
MANOKSHA_BOOTSTRAP_OWNER_PASSWORD='…' dotnet Manoksha.Api.dll bootstrap-owner --email owner@example.com --name "Owner"
```

## Tests

```bash
cd manoksha-backend
dotnet test                                    # unit + architecture + integration (Testcontainers Postgres 16)
MANOKSHA_TEST_POSTGRES="Host=localhost;Port=5432;Username=…;Database=postgres" dotnet test   # use an existing server
UPDATE_OPENAPI_CONTRACT=1 dotnet test tests/Manoksha.IntegrationTests   # after an intentional API change…
npm run api-client:generate                                             # …then regenerate TS types
```

## Implementation status

| Phase | Scope | Status |
|---|---|---|
| 1 | Foundation: solution/modules, PostgreSQL + migrations, identity (internal login, MFA, mobile OTP), RBAC with branch scope, audit, settings, outbox, idempotency, admin-web shell, CI | **Done** |
| 2 | Branches, fulfillment priority, employees, attendance, catalog (configurable variant attributes, SKUs, barcodes, label printing) | **Done** |
| 3 | Suppliers, purchase orders (amend/close), goods receipt, hybrid stock (pieces + quantities), FIFO cost layers per SKU per branch, transfers, blind counts, adjustments with value-based approval, discrepancies | **Done** |
| 4 | Retail price history, product reseller discounts, reseller pricing calculator, reseller onboarding (PENDING → OTP → ACTIVE), status rules, versioned commercial terms, ₹0 wallet | **Done** |
| 5 | Append-only wallet ledger, proof-based deposits with idempotent Owner approval, Owner adjustments, reseller checkout (branch priority, no split, atomic stock + wallet + order, idempotent), fulfilment inquiries, reseller end-customers, reseller web area | **Done** |
| 6 | Customer storefront (anonymous browsing, OTP sign-in/registration, cart, checkout), complete-basket branch routing with 5-minute reservation, UPI payment pipeline (gateway adapter + simulator, signed webhook inbox, poller), late-success recovery or reconciliation case with Owner actions/refund markers and a critical alert banner, provider-confirmed reseller wallet deposits, customer profile, Owner payments view | **Done** (simulator; real gateway adapter pending Owner choice) |
| 7 | Fulfillment: branch queue, processing/packed/shipped (courier + optional tracking)/delivered, fulfillment exceptions, whole-order reroute with stock return and discrepancies, administrative cancellation (wallet reversal / payment reconciliation), customer & reseller order tracking | **Done** (courier APIs later) |
| 8 | Flutter POS | Next |
| 9–10 | See design §22 | Planned |
