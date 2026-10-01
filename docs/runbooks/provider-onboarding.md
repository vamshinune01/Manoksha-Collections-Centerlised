# Connecting real providers

Until the Owner chooses providers, staging uses stand-ins: **fake SMS** (OTP codes in Cloud Logging), **logging email** (every email
is stored and viewable in Admin → Notifications → Email log) and the **UPI simulator**. Production refuses all three at start-up.

Each real provider is one adapter class behind an existing interface plus configuration — no business logic changes:

| Integration | Interface | What the Owner provides | Notes |
|---|---|---|---|
| SMS (OTP only) | `ISmsSender` | Provider account (e.g. MSG91, Gupshup), **DLT** entity + sender ID + approved OTP template | Template text must match `"{code} is your Manoksha Collections verification code…"` |
| Email | `IEmailSender` | Provider (e.g. Amazon SES, Brevo, Postmark), sending domain with SPF/DKIM/DMARC | Customer/reseller emails and Owner alerts (ADR-001 §37, §39) |
| UPI payments | `IPaymentGateway` | Gateway (e.g. Razorpay, PhonePe PG, Cashfree) merchant account, API keys, webhook secret | Webhook URL: `https://<api>/api/v1/webhooks/payments/<provider>`; webhooks are verified, then re-checked server-to-server |

Steps once chosen: developer adds the adapter + tests against the provider's sandbox → secrets added (secret-rotation.md) →
staging switched (`Integrations__<Kind>__Provider`) and tested end to end → production.
