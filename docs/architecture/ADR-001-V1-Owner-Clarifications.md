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
