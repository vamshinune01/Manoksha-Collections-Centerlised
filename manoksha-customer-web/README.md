# manoksha-customer-web

Public storefront (mobile OTP login, catalog, cart, checkout with ₹100 shipping per order, UPI payment, order history and
tracking, "Need help with this order?" WhatsApp link — no self-service cancellation) and the separated, authenticated
`/reseller` area (reseller prices, wallet, deposits, orders, end-customers).

Next.js + TypeScript + Tailwind, same BFF/session pattern as `manoksha-admin-web`, separate token audiences
(`customer`, `reseller`). Scaffolded in **Phase 5 (reseller area)** and **Phase 6 (storefront)**; the backend OTP
authentication it will use is already implemented (`/api/v1/auth/otp/*`, `/api/v1/auth/customer/register`).
