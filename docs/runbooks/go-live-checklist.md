# Production go-live checklist

Tick every line before the first real customer. Items marked **Owner** need a decision or an account from the Owner.

## 1. Decisions and accounts (Owner)
- [ ] **Domains** for the shop (e.g. `manokshacollections.com`), admin (`admin.…`) and API (`api.…`); DNS access.
- [ ] **Production database**: Supabase **Pro** (daily backups, point-in-time recovery, no pausing) or **Cloud SQL** PostgreSQL 16
      (automated backups + PITR). The free tier is not acceptable for real orders (database-backup-restore.md).
- [ ] **SMS provider** with DLT registration (entity, sender ID, OTP template approved).
- [ ] **Email provider** and sending domain (SPF, DKIM, DMARC published).
- [ ] **UPI payment gateway** merchant account (live keys, webhook secret, settlement bank account).
- [ ] Separate **production GCP project** with billing, and a **budget alert** (e.g. ₹3,000/month) emailed to the Owner.

## 2. Infrastructure
- [ ] `terraform apply -var-file=envs/production.tfvars` (infra/terraform) — API (min 1 instance), web apps, Worker, migration job,
      buckets, secrets, uptime checks and alerts.
- [ ] Every secret has a value (secret-rotation.md): DB connection, signing key, data key, OTP pepper, **proxy key** (≥ 32 chars,
      same for API and both web apps), provider keys. No secret is in the repository or in chat history.
- [ ] Custom domains mapped to the three services; HTTPS certificates issued; old `run.app` URLs not given to customers.
- [ ] Storage CORS allows only the admin domain for uploads.
- [ ] Alert email channel verified (Google sends a confirmation); a test alert received.

## 3. Security
- [ ] `ASPNETCORE_ENVIRONMENT=Production` on API and Worker (enables: Owner MFA required, Swagger off, development adapters refused,
      proxy key required).
- [ ] Owner account created through the one-time setup page, **MFA enrolled**, setup code then rotated/disabled.
- [ ] Staff accounts created by invite link; roles and branches reviewed (Users → each user); nobody has Owner except the Owner.
- [ ] Approval PINs set by managers and the Owner.
- [ ] CI green, including the **authorization sweep** (every endpoint refuses the wrong caller), the dependency vulnerability scan
      and the concurrency/failure suites.
- [ ] Rate limits reviewed for expected traffic (`RateLimiting:*`; defaults: 600/min per signed-in user, 300/min per anonymous IP,
      20/min sign-in and 10/min OTP per IP).

## 4. Data
- [ ] Branches, fulfillment priority, categories, attributes, products, SKUs, barcodes and **retail prices** entered.
- [ ] **Opening stock**: goods receipts (with FIFO cost) per branch, then a stock count to confirm.
- [ ] Business settings reviewed: reservation minutes, shipping fee, POS discount limits (5% / 15%), payment methods, low-stock
      threshold, daily summary hour, WhatsApp number.
- [ ] Resellers onboarded with commercial terms; wallets funded only through approved deposits.
- [ ] No test data in production (staging and production databases are separate).

## 5. Operations rehearsal (on staging, then production)
- [ ] **Backup + restore rehearsed** into an empty database (database-backup-restore.md); first production backup taken.
- [ ] **Load test** passes against staging: `BASE_URL=… VUS=30 DURATION=120 node tests/load/load-test.mjs` (p95 < 800 ms, < 1% errors).
- [ ] **Rollback rehearsed** once (deploy-and-rollback.md).
- [ ] End-to-end sale on each channel with real providers in their sandbox: online UPI order → pack → ship → deliver (emails
      received); reseller wallet deposit → approve → order; POS sale with a discount needing approval, printed receipt.
- [ ] A failed-payment and a late-payment case reproduced and reconciled from the Exception Center.

## 6. Devices and staff
- [ ] POS app installed on each counter phone (release build, signed with the Owner's keystore kept offline); Bluetooth printer
      paired and test receipt printed; staff signed in once.
- [ ] Staff walked through: POS sale and bargaining limits, attendance, packing orders, fulfillment exceptions, stock counts.
- [ ] The Owner walked through: Dashboard, Exception Center, Notifications, Reports, wallet deposits, reconciliation.

## 7. Launch day
- [ ] Deploy the release tag; migrations job succeeded; smoke test passed (deploy-and-rollback.md §5).
- [ ] Watch Cloud Logging and the Exception Center for the first hours; previous revision noted for rollback.
- [ ] After a week: review the error alerts, daily summaries, and adjust rate limits/low-stock threshold if needed.
