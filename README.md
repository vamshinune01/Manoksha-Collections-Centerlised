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
| [`manoksha-customer-web/`](manoksha-customer-web) | Public storefront + separated `/reseller` area | Next.js (Phase 5–6) |
| [`manoksha-mobile-pos/`](manoksha-mobile-pos) | Store POS, scanning, stock operations | Flutter (Phase 8) |
| [`packages/api-client/`](packages/api-client) | TypeScript types generated from the backend OpenAPI contract — no business logic | openapi-typescript |
| [`contracts/openapi/`](contracts/openapi) | Committed OpenAPI snapshot; CI fails if it drifts from the API | |
| [`infra/`](infra) | Local docker compose, database roles, (Phase 10) Terraform for GCP | |

All business rules and authorization live in the backend. Frontends only display backend-authoritative data.

## Local development

Prerequisites: .NET SDK 8, Node 20.9+, Docker.

```bash
# 1. Database (Postgres 16 on localhost:5433). Mailpit is optional: add "mailpit" to also start it.
docker compose -f infra/docker-compose.local.yml up -d postgres

# 2. Migrate + create local dev users (choose your own local password)
export MANOKSHA_DEV_SEED_PASSWORD='choose-a-local-passw0rd'
dotnet run --project manoksha-backend/src/Hosts/Manoksha.Api -- seed-dev

# 3. API on http://localhost:5080 (Swagger UI at /swagger) and the Worker
dotnet run --project manoksha-backend/src/Hosts/Manoksha.Api
dotnet run --project manoksha-backend/src/Hosts/Manoksha.Worker

# 4. Admin web on http://localhost:3001
npm install
cp manoksha-admin-web/.env.example manoksha-admin-web/.env.local
npm run admin:dev
```

The dev seed creates branches Karimnagar (P1), Hyderabad (P2), Mulugu (P3) and users (password = `MANOKSHA_DEV_SEED_PASSWORD`):
`owner@manoksha.local`, `manager.karimnagar@manoksha.local`, `sales.karimnagar@manoksha.local`, `inventory.karimnagar@manoksha.local`
(the three Karimnagar users also have employee profiles). Development OTP codes are printed in the API log
(`[FAKE SMS]`). Development-only adapters (fake SMS, logging email, local file storage) are refused in Production.

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
| 3 | Suppliers, purchasing, goods receipt, FIFO inventory, transfers | Next |
| 4–10 | See design §22 | Planned |
