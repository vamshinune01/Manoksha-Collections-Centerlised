# ADR-001 — V1 Owner Clarifications (authoritative)

| | |
|---|---|
| Status | **ACCEPTED** (Owner, 24 Sep 2026) |
| Applies to | [V1-Architecture-and-Design.md](V1-Architecture-and-Design.md) — architecture approved as proposed |
| Precedence | SPEC (business source of truth) → this ADR (authoritative clarifications of SPEC) → design document |

These answers resolve Q1–Q12 and the setup items of the design document §23.

## 1. Repository layout
One V1 monorepo. The current workspace folder is the repository root, containing `manoksha-backend/`, `manoksha-admin-web/`, `manoksha-customer-web/`, `manoksha-mobile-pos/`, `infra/`, `contracts/`, `docs/`. The four apps stay logically and deployably separate.

## 2. Customer and reseller mobile numbers
The same mobile number may exist as both a Customer identity and a Reseller identity — separate account contexts, permissions and tokens. Customer authentication never grants reseller access; reseller authentication never grants customer-context permissions.
**Implementation:** uniqueness is `(account_type, mobile_e164)`; OTP challenges and tokens are bound to one account context (`customer` / `reseller` audience).

## 3. FIFO costing
FIFO cost layers **per SKU per branch**. A transfer carries the originating cost basis (the consumed source layers' unit costs and dates) into the destination branch's layers, so company cost is unchanged by transfers.

## 4. Goods receipt
Supplier delivers directly to the receiving branch. A Purchase Order identifies the expected receiving branch. An authorized Inventory Employee / Branch Manager verifies and records the Goods Receipt → inventory created → becomes AVAILABLE after successful receipt/verification. Owner sees all receipts. Later movement between branches uses the normal transfer workflow. No central warehouse.

## 5. Reseller end-customers
Relationship model: `Reseller → ResellerCustomerRelationship → Customer/Contact`. A normalized central person/contact identity may be kept internally for deduplication, but relationships and order history are isolated per reseller and never merged automatically. Reseller A can never search/browse/access Reseller B's relationships or orders. Owner has global visibility.

## 6. Reseller catalog
Products carry `AvailableForRetail` and `AvailableForReseller`. Products not available for resellers do not appear as purchasable in the reseller catalog, and the backend rejects any reseller order line for them.

## 7. Transfer approval
Created by an authorized employee/manager; the **source-branch manager** approves release; the destination verifies/receives. Owner can approve/manage globally. No employee or manager approves their own request.
**Owner exception:** an Owner-created transfer may be authorized by the same Owner. RequestedBy, ApprovedBy, ApprovalTime, Source, Destination, Items and Reason are still recorded, and the audit entry explicitly marks *Owner initiated and authorized*.

## 8. Administrative cancellation
No customer cancellation. Owner always may cancel; a Branch Manager may cancel orders assigned to their branch only with the configured permission. Cancellation requires a reason and is audited.
- **Online paid order:** stop fulfillment, release RESERVED / unconsumed allocation, and create `PAYMENT_RECONCILIATION_REQUIRED` when money was received — never mark as refunded unless an external refund is recorded.
- **Reseller wallet order:** a system-generated REVERSAL/CREDIT ledger entry linked to the order and original debit (original never changed); no manual Owner balance edit required.
- **Inventory:** safely releasable stock → AVAILABLE; DAMAGED / LOST / missing stock is never returned to AVAILABLE by cancellation. Every change keeps movement history.

## 9. POS sales
A POS sale is a store order that becomes `COMPLETED` on successful finalization, at retail pricing, with authorized bargaining and split payments. Reseller pricing never applies automatically at POS.

## 10. Customer checkout login
Browsing and cart building may be anonymous; order placement/payment requires customer login (mobile OTP). No guest orders.

## 11. Monetary rounding
Fixed-decimal types only. Monetary results rounded to 2 decimals (paisa) using HALF-UP (e.g. ₹1,234.567 → ₹1,234.57). No whole-rupee rounding. Percentages stored with sufficient precision. All authoritative rounding in the backend.
**Implementation:** `numeric(14,2)` for money, `numeric(7,4)` for percentages, `MidpointRounding.AwayFromZero` (equivalent to half-up for the non-negative amounts used).

## 12. Late payment recovery price
Recovery preserves the original price snapshot the customer paid. It revalidates inventory, complete-basket fulfillment, branch eligibility, payment identity, payment amount and the original snapshot, and never renegotiates price. Not recoverable → `PAYMENT_RECONCILIATION_REQUIRED`.

## Setup / providers
Payment gateway, SMS OTP/DLT, email, domains and GCP billing are not Phase 1 blockers. All are behind adapters (`IPaymentGateway`, `ISmsSender`, `IEmailSender`, `IFileStorage`) with safe development fakes; the fakes are refused at startup in Production.

## Engineering notes recorded during Phase 1
- Web workspaces use **npm workspaces** instead of pnpm (pnpm/corepack is not available on the build machine with Node 25). No functional impact.
- Entity IDs are server-generated UUIDv7 `Guid`s (strongly-typed ID wrappers were dropped to keep EF mappings simple; IDs remain immutable and server-generated).
- Application services are called directly (no mediator library); behaviour is equivalent.
- Audit rows carry a content hash in Phase 1; the cross-row hash chain / sealing is delivered in Phase 10 hardening.
- Next.js 16 renamed Middleware to **Proxy** (`src/proxy.ts`); the admin web uses it for silent token refresh.
- ESLint 10 is used with an explicit React version setting (eslint-plugin-react's auto-detection is incompatible with ESLint 10).
- Configuration arrays merge by index in .NET, so environment-specific arrays (e.g. `Auth:MfaRequiredRoles`) are defined only in
  the environment files that need them; `EnvironmentConfigurationTests` guards this (Owner MFA required in Staging/Production).
- Modules register HTTP-only services (authentication/authorization) through `IModule.AddApiServices`, so the non-web Worker host
  never builds them; `HostCompositionTests` guards this.

## Engineering decisions recorded during Phase 2 (confirm or correct)
- **Fulfillment priority always lists every branch.** A newly created branch is appended at the lowest priority (new, audited version);
  the Owner reorders the full list. Inactive branches stay in the list and are skipped during routing.
- **Nobody corrects their own attendance.** Corrections need `attendance.correct` for that branch, a reason, and are audited.
- **Employee reassignment** requires `employees.manage` for both branches and no open clock-in. It does not change role
  assignments, which remain Owner-only.
- **Tracking mode** (per piece vs by quantity) can change only while a product is a draft.
- **One SKU per variant**; a product without variant attributes has exactly one "Standard" variant.
- **Barcodes:** internal codes are EAN-13 in the in-store `29` prefix range; supplier codes are check-digit validated; a code
  is never reused, even after retirement. Label reprints are logged (append-only) and never create a new identity.
- Serialized item-level barcodes are created at goods receipt (Phase 3); scan results gain location/status (Phase 3) and
  applicable price (Phase 4).

## Phase 3 Owner decisions (25 Sep 2026)
13. **Over-receipt is blocked.** A goods receipt cannot exceed the remaining ordered quantity; the PO must first be amended
    (audited) by someone with purchasing permission.
14. **Adjustment approval threshold = value only** (quantity × unit cost at FIFO/purchase cost), configured by the Owner in
    `inventory.adjustment.manager_max_value`. Default **₹0**, so every adjustment needs Owner approval until the Owner raises it.
    Within the limit, a user with `inventory.adjust.approve` for that branch may approve.
15. **No self-approval of adjustments — including the Owner.** The approver must always be a different authorized person
    (the Owner self-authorization exception applies to transfers only, §7).
16. **Found stock cost:** the approver must enter the unit cost when approving a "found" adjustment; that cost creates the
    FIFO layer and is used for the threshold check.

Engineering decisions for Phase 3 (confirm or correct):
- Transfer receipt cannot exceed the dispatched quantity; missing units create a discrepancy (resolved as received late,
  returned to source, or written off). Extra units found later are handled through a stock count.
- FIFO layers cover all owned on-hand units at a branch (any status except sold/in transit). A transfer consumes the source's
  oldest layers at dispatch and recreates them at the destination with the original unit costs and dates.
- Stock counts are blind (counters do not see the system quantity until submission). Differences become discrepancies,
  resolved by an approved adjustment or dismissed with a reason.

## Phase 4 Owner decisions (25 Sep 2026)
17. **Reseller sign-in by status:** ACTIVE — full access; FROZEN — read-only sign-in (history, wallet, terms; no new orders or
    deposits); SUSPENDED — no sign-in (existing sessions are revoked when suspended); CLOSED — read-only sign-in to their own history.
    PENDING — sign-in is the activation step (mobile OTP).
18. **A reseller's registered mobile number cannot be changed in V1.** To change it, close the reseller and create a new one.
19. **A PENDING reseller may be closed** by the Owner (with a reason, audited); the record is kept.

Engineering decisions for Phase 4 (confirm or correct):
- Activation "eligibility checks" = the reseller is still PENDING, has an initial commercial-term version and a ₹0 wallet.
- Retail prices are set per SKU and take effect immediately; a SKU without a retail price is not sellable in any channel.
- Discount percentages allow up to 2 decimals (0–100). Reseller price = retail × (1 − discount/100), rounded HALF-UP to paisa.
- Commercial-term PDF/email notifications are delivered with the notifications module (Phase 9); every term version is stored now.
- The reseller wallet (₹0) is created at onboarding; its ledger, deposits and checkout arrive in Phase 5.

## Phase 5 Owner decisions (25 Sep 2026)
20. **Direct/PhonePe deposit proof:** both the payment reference (UTR/transaction id) **and** a screenshot are mandatory.
21. **Every reseller order needs delivery details entered at checkout** — name, mobile and full address — whether the order is
    for an end-customer or for the reseller themselves. The details are snapshotted on the order.
22. **End-customer details:** name, mobile and full delivery address (line, city, state, PIN) are required; email optional. Saved
    only to that reseller's own customer list.

Engineering decisions for Phase 5 (confirm or correct):
- A payment reference can back only one pending/approved deposit (prevents the same payment being credited twice).
- Provider-confirmed online deposits (SPEC §17.1) are delivered with the UPI gateway integration in Phase 6.
- The reseller cart lives in the browser (non-authoritative); the backend re-prices and re-validates everything at checkout.
- A reseller order that no branch can fulfil completely is not created and nothing is debited; a fulfilment inquiry
  reference (MC-FUL-XXXXXXXX) is issued with the WhatsApp help link, as for customers (SPEC §12).
- Reseller orders commit stock immediately (AVAILABLE → SOLD) together with the wallet debit and order creation; FIFO cost is
  consumed at that point for gross-profit reporting.
- The Owner's manual wallet adjustment (credit or debit) is Owner-only with a mandatory reason, and can never take the balance below ₹0.

Phase 5 completion items (engineering, 25 Sep 2026):
- The wallet reversal used by administrative cancellation (§8) locks the wallet and refuses a second reversal of the same
  debit (`WALLET_DEBIT_ALREADY_REVERSED`); the original debit is never changed.
- A nightly wallet integrity check (Worker, `Jobs:WalletIntegrityIntervalHours`, default 24) verifies cached balance = ledger
  credits − debits, the last entry's balance, and the unbroken balance chain. Findings are audited
  (`wallet.integrity.mismatch`) and published as an event for the Owner; the check never auto-corrects. The Owner can also run
  it on demand (`GET /api/v1/admin/wallet/integrity`).
- The Owner sees each reseller's saved end-customers on the reseller page (§5 global visibility); resellers add and edit their
  own saved customers in the reseller web area.

## Engineering decisions recorded during Phase 6 (confirm or correct)
- **UPI gateway:** built behind `IPaymentGateway` with a development **simulator** (refused in Production). The real provider
  adapter is added once the Owner selects a gateway; nothing else changes. A webhook is only a trigger — the backend always
  re-queries the provider server-to-server and checks amount/currency before acting.
- **Payment attempt amount = order grand total (merchandise + ₹100 shipping)**, set by the backend. No extra payment limits are
  invented; the provider's own limits apply (a rejected session fails safely and releases the reservation).
- **Online order lifecycle:** checkout reserves the complete basket (AVAILABLE → RESERVED) at the first priority branch that can
  fulfil it, for `reservation.minutes` (default 5). Payment success within the window sells the reserved stock and consumes FIFO
  cost; failure releases immediately; expiry releases via the Worker sweeper (every 15 s).
- **Late success (SPEC §14.2):** any success after the window, or after a FAILED/EXPIRED status, is rechecked: stale holds are
  released, then Owner priority is re-run for the snapshotted basket at the original prices. The order may be recovered at a
  different branch than originally reserved. If no branch can fulfil it → `PAYMENT_RECONCILIATION_REQUIRED` + reconciliation case
  (`MC-REC-…`), audited, CRITICAL event for the Owner.
- **Owner alert (until notification channels arrive in Phase 9):** a red banner on every admin page while any reconciliation case is
  open, plus a CRITICAL log entry from the Worker (usable for log-based alerting in GCP).
- **Reconciliation actions (SPEC §36), Owner-only:** note, Refund Initiated, Refund Completed (external refund reference required),
  Resolved; append-only history + audit. No money is moved by the application.
- **Customer profile:** customers edit their name and email; the mobile number (sign-in identity) is read-only.
- **Paid amount ≠ amount due** → never confirmed; the hold is released and a reconciliation case (`AMOUNT_MISMATCH`) is opened.
- **Missed webhooks:** the Worker polls live attempts every 30 s and failed/expired ones every 5 min for 24 h after initiation.
- **A failed payment is not retried on the same order**: stock is released immediately (SPEC §13); the customer checks out again.
- **Customers see their own online orders**, including ones whose payment did not complete (clearly labelled). Checkout is
  prefilled from the customer's latest delivery details; there is no separate address book in V1.
- **Storefront stock** shows only an "in stock" hint (some branch has AVAILABLE units); checkout decides per branch.
- **Online reseller deposits (SPEC §17.1):** amount chosen by the reseller; payment window setting `payments.online_deposit_minutes`
  (default 15). Credited once, only after provider confirmation (unique ledger link); a success after the window is still
  credited because the money was received. Not available while the reseller is FROZEN/SUSPENDED/CLOSED (ADR-001 §17).
- Product images/descriptions are not yet shown on the storefront (catalog image management was not part of Phases 2–6); cards
  show the product name, variant and price.

## Phase 7 Owner decisions (27 Sep 2026)
23. **Shipping details:** when an order is marked SHIPPED the **courier is required** (Xpressbees, Delhivery or Other with a name);
    the **tracking number is optional**.
24. **Cancellation window:** the Owner (or a manager granted `orders.cancel`) may cancel while the order is Confirmed, Processing,
    **Packed** or in Fulfillment Exception. Once Shipped it cannot be cancelled (returns remain postponed in V1).
25. **Stock on cancel/reroute: automatic return** — all units go back to AVAILABLE except units staff mark as damaged (→ DAMAGED)
    or missing (→ written off as LOST with an inventory discrepancy for investigation).
26. **Delivery:** Xpressbees and Delhivery will be integrated later. Until then staff mark an order DELIVERED (date + optional note);
    the courier integration will use the same step.

Engineering decisions for Phase 7 (confirm or correct):
- The branch queue shows only confirmed orders (never unpaid online orders), oldest first.
- Order steps need `orders.fulfill` for the order's branch; reporting a problem needs `orders.fulfillment_exception.raise`;
  reroute and "resolved at this branch" need `orders.reroute`; cancel needs `orders.cancel` (Owner by default for the last two).
- A reroute requires an open fulfillment exception and a target branch that can fulfil the **complete** order; the target's stock
  is sold at reroute time and the original branch's stock returns per decision 25. Prices, payment and wallet are unchanged.
  If no branch can fulfil, the exception stays open (amber banner on every admin page) until it is resolved in place or cancelled.
- Returned units get their original FIFO cost layers back (same unit cost and layer date); missing units stay consumed (a loss)
  and their ORDER discrepancy is closed with a reason once investigated — stock found later comes back via a "found" adjustment.
- Cancelling a reseller order credits the wallet with a linked REVERSAL entry; cancelling a paid online order opens a payment
  reconciliation case (`ORDER_CANCELLED_AFTER_PAYMENT`) and is never marked refunded without an external refund reference.
- Customers and resellers see order progress, courier and tracking number, but never internal notes.

## Owner architecture directive (28 Sep 2026)
27. **Keep the approved architecture:** .NET 8 modular monolith + EF Core + **PostgreSQL** as the only primary database. No
    Firestore / Firebase Realtime Database. No Kubernetes, Kafka, Redis, microservices or other unnecessary infrastructure.
28. **Portable database hosting:** PostgreSQL locally; initial production may use a low-cost/serverless PostgreSQL provider and
    move to GCP Cloud SQL later. The application uses standard PostgreSQL 16 only (no provider-specific features); the provider
    is chosen purely by the connection string (Secret Manager). Provider requirements: PostgreSQL 16, TLS, and a **direct or
    session-mode** connection (not transaction-mode pooling) because background jobs use session advisory locks.
29. **Media:** product HD images, thumbnails and videos live in **Google Cloud Storage**; PostgreSQL stores only media metadata
    (type, size, dimensions, duration, order, alt text) and object paths/URLs. Originals are kept private; customers are served
    **optimized renditions** (WebP images in several sizes, 720p H.264 MP4 video with a poster), never the large originals.
30. **Scale target:** about 300 customers, 50 resellers and 3 branches — size everything for low cost (scale-to-zero services,
    smallest database tier, no always-on extras).

## Owner decisions (29–30 Sep 2026): sign-up and sign-in
31. **Owner MFA:** not required on **Staging** (direct email + password); still required in **Production** (Owner's own change).
32. **Account creation / sign-up:**
    - **Owner:** a one-time **first-setup page** (`/setup` on the admin site) creates the Owner only while no Owner exists and only
      with the setup code held in Secret Manager (`Setup:OwnerSetupCode`); it closes permanently after that.
    - **Staff (managers, employees):** no open sign-up (SPEC §5.1). The Owner adds the person and shares a one-time **invite link**
      (valid 72 h, single use; a new link revokes older ones) where they set their own password. The temporary-password option remains.
    - **Customers:** self sign-up by mobile OTP, now shown as "Create account" in the shop.
    - **Resellers:** unchanged — Owner-created only, activated by mobile OTP (SPEC §5.3).

## Owner decisions (30 Sep 2026): Phase 8 — store POS
33. **Authorized bargaining limits** (settings, Owner-editable): a sales employee may lower a price by up to
    `pos.staff_max_discount_pct` (**5 %**) alone; up to `pos.manager_max_discount_pct` (**15 %**) needs a branch manager; more
    than that only the **Owner**. A reason is always required and a price can never exceed retail. Every change is recorded
    (append-only `orders.pos_price_overrides`: original and final price, %, reason, seller, approver, level).
34. **Approval on the same device by PIN:** the approver picks their name on the POS and enters their own 4–6 digit approval PIN
    (hashed; 5 wrong tries lock it for 15 minutes; set under *My account* on the admin site or *Settings* in the app with the
    current password). Nobody approves their own sale.
35. **Payments:** **UPI only** for now — the customer pays the shop QR and staff enter the UPI transaction reference.
    Accepted methods are the `pos.payment_methods` setting so CASH, CARD and OTHER can be switched on later; split payments are
    supported and must add up exactly to the bill. Non-cash payments always need a reference.
36. **Device:** Android phones/tablets with a paired **Bluetooth ESC/POS thermal printer** (58 mm or 80 mm). A sale needs an
    internet connection; the server validates prices, limits, PIN and stock and completes the sale in one transaction
    (`MC-POS-…`, idempotent per basket so a retry never sells twice).

## Owner decisions (30 Sep 2026): Phase 9 — notifications, Exception Center, reporting
37. **Customer / reseller messages: email only.** Order confirmed, shipped (courier + tracking), delivered, cancelled; wallet
    deposit credited or rejected; new commercial terms; reseller account status. Sent only when the person has an email address
    (online customers: the checkout email, else their profile email; resellers: their profile email). **SMS stays OTP-only**
    (no DLT templates needed beyond OTP). Everything is also visible in the customer/reseller account.
38. **Low stock: one global threshold** — the setting `inventory.low_stock_threshold` (default **2**): a SKU is low at a branch when
    its available quantity is at or below it. Shown on dashboards/reports; branch staff with `inventory.view` get one in-app
    notification per branch per day (from 09:00 IST).
39. **Owner email:** immediately for **CRITICAL** events (payment needs reconciliation, fulfillment exception, sensitive alert:
    staff sign-in or approval-PIN lockout, wallet integrity mismatch) plus **one daily summary** at `notifications.daily_summary_hour`
    (default **21:00 IST**). Everything else is in-app (the bell) and in the Exception Center.
40. **Branch managers never see cost:** dashboards and reports show them sales, stock counts, low stock, transfers, approvals and
    attendance for their branches only; FIFO cost, gross profit, margin, stock value and reseller reports are Owner-only
    (`reports.global`).

### Phase 9 engineering notes
- **Providers are mocked until chosen:** email uses the `Logging` provider (Staging/Development; refused in Production) and every
  email is stored in `notifications.email_deliveries`, viewable by the Owner (*Notifications → Email log*, with preview). Adding the
  real provider = one `IEmailSender` adapter + configuration; no business code changes.
- **Delivery never affects the business transaction (SPEC §29, §33):** business events go to the outbox in the business
  transaction; handlers only create notification/email/alert rows (idempotent dedupe keys); the email job sends with backoff
  (1, 5, 15, 60, 180 min; 6 attempts), then the email becomes a **Failed Notification** in the Exception Center with manual retry.
- **Exception Center = one query over the authoritative records** (reconciliations, fulfillment exceptions, inquiries,
  discrepancies, deposits, failed emails, alerts) instead of a separate `exception_cases` index table: nothing extra to keep in sync
  at this scale. Items carry severity and branch; visibility follows the viewer's permissions and branch scope.
- **Reporting** reads across schemas with read-only SQL (no tables of its own). A sale counts on its confirmation date (IST);
  cancelled orders are excluded; revenue excludes shipping; gross profit = revenue − FIFO cost of the order's current allocation.
- Business events moved to each module's `Contracts` namespace so the Notifications module consumes them without touching module
  internals. New events: inventory discrepancy opened, transfer requested, adjustment requested, security alert (lockouts).
- Unfulfilled checkouts can now be closed after support follow-up (note required, audited).

## Phase 10 — hardening (1 Oct 2026, engineering decisions within the Owner's cost directive §27–30)
- **No Cloud Armor / external load balancer for now** (≈ USD 20+/month): the API enforces its own limits — per signed-in user
  600 requests/min, per anonymous client IP 300/min, sign-in 20/min and OTP 10/min per IP (all settings). Add Cloud Armor later if
  traffic or attacks justify it.
- **Real client IP behind the web apps:** browsers reach the API through the Next.js servers, so the web apps forward the browser's
  IP with a shared secret (`Security:ProxyKey` / `MANOKSHA_PROXY_KEY`, Secret Manager). The API trusts the IP only with the key;
  required in Production. Fixes rate limits shared by all users and login/audit history showing the server's IP.
- **Authorization sweep test:** every endpoint is checked automatically — only a fixed list may be anonymous, every route is bound
  to its token audience, every admin route needs a permission (fifteen routes that relied only on in-service checks now also
  require a permission at the endpoint), and real requests from anonymous, customer, reseller and under-privileged staff callers are
  refused.
- **Sign-in and approval-PIN counters are atomic database updates:** simultaneous sign-ins never fail with a conflict (which could
  also reveal a correct password) and parallel guesses are all counted toward the lockout.
- **Security headers:** API — no framing, no caching of personal data, HSTS, `default-src 'none'` CSP on JSON; web apps — CSP
  (own code only, product media from Cloud Storage), HSTS, no framing. Request bodies limited to 10 MB (media goes straight to
  Cloud Storage).
- **Storage outage** during a deposit upload returns "try again" (503) and records nothing.
- Infrastructure as code (`infra/terraform`), runbooks (`docs/runbooks`) and the production go-live checklist added; the free
  Supabase tier is not acceptable for production (no backups, pauses when idle) — Owner to choose Supabase Pro or Cloud SQL.
- **Owner decision (1 Oct 2026): Terraform is used at production go-live** to create the production project (services, buckets,
  secrets, Worker, migration job, uptime checks and alerts). Staging stays as built by hand until then; the Terraform files are
  validated (`terraform validate` + `plan`) as part of the go-live work, not before.
