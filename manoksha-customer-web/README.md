# manoksha-customer-web

Next.js app for external users. Talks only to the central backend (`MANOKSHA_API_URL`, server-side).

- **`/reseller/*` — reseller area (Phase 5, live):** mobile OTP sign-in (first sign-in activates an Owner-created reseller),
  dashboard, priced catalog, cart (per-browser, non-authoritative) and checkout with an Idempotency-Key, orders with the
  "Need help with this order?" WhatsApp link (no self-cancellation), wallet ledger, deposit requests with reference + screenshot,
  own customer list, commercial-term history. Separate `reseller` token audience and cookies (`mk_rs_*`).
- **Storefront — Phase 6:** customer OTP login, browsing, cart, checkout with 5-minute reservation and UPI payment.

```bash
cp .env.example .env.local
npm run customer:dev   # http://localhost:3002
```
