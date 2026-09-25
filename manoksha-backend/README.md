# manoksha-backend

The single business backend for every channel (admin web, customer web, reseller area, Flutter POS). Modular monolith
on .NET 8 + EF Core + PostgreSQL. See the approved design: [../docs/architecture/V1-Architecture-and-Design.md](../docs/architecture/V1-Architecture-and-Design.md).

## Layout

```
src/
  BuildingBlocks/
    Manoksha.SharedKernel      UUIDv7 ids, Money (HALF-UP paisa), MobileNumber, errors, IClock
    Manoksha.Application       cross-module abstractions: ICurrentUser, IPermissionService, IAuditWriter, IOutbox,
                               IIdempotencyService, ISettingsReader, integration ports, permission catalog, system roles
    Manoksha.Persistence       ManokshaDbContext (one DB, schema per module), unit of work, outbox, idempotency, append-only SQL
  Modules/                     one project per bounded module; other modules may use only its Contracts
    Manoksha.Modules.Identity  internal login + MFA, customer/reseller OTP, sessions, RBAC
    Manoksha.Modules.Audit     immutable audit log + search
    Manoksha.Modules.Settings  Owner-managed business settings (versioned, audited)
    Manoksha.Modules.Branches  branch master, versioned fulfillment priority (IBranchDirectory, IFulfillmentPriorityProvider)
    Manoksha.Modules.Employees employee profiles, branch assignment history, attendance
    Manoksha.Modules.Catalog   categories, configurable variant attributes, products, variants, SKUs, barcodes (ICatalogLookup)
    Manoksha.Modules.Inventory stock levels, serialized pieces, FIFO cost layers, movements, transfers, counts, adjustments,
                               discrepancies; StockEngine is the only code that changes stock (IStockReceiver)
    Manoksha.Modules.Purchasing suppliers, purchase orders, goods receipts (posts to Inventory in the same transaction)
  Integrations/                ISmsSender / IEmailSender / IFileStorage adapters (dev fakes refused in Production)
  Migrations/                  EF Core migrations
  Hosts/
    Manoksha.Hosting           shared composition (ModuleCatalog)
    Manoksha.Api               HTTP API + CLI commands (migrate, bootstrap-owner, seed-dev)
    Manoksha.Worker            outbox dispatcher, housekeeping (later: reservation expiry, payment polling)
tests/
  Manoksha.UnitTests, Manoksha.ArchitectureTests (module boundaries), Manoksha.IntegrationTests (real PostgreSQL)
```

## Conventions

- Every endpoint requires authentication unless it explicitly allows anonymous; admin routes also require the `admin`
  token audience and a named permission. Branch-scoped checks are repeated in services.
- Sensitive changes call `IAuditWriter` inside the same transaction. `audit.audit_log`, `settings.system_setting_changes`
  and `identity.login_events` are append-only at the database level.
- Money is `decimal` / `numeric(14,2)`, rounded HALF-UP to paisa via `Money`.
- Business errors are `BusinessRuleException(code, message, status)` → RFC 7807 problem details with `code`.

## Adding a migration

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/Migrations/Manoksha.Migrations --startup-project src/Migrations/Manoksha.Migrations --output-dir Migrations
```

New modules must also be added to `ModuleCatalog` and `DesignTimeDbContextFactory`. Cross-module foreign keys are added with
raw SQL in the migration (modules stay decoupled in code; the database still enforces references).
