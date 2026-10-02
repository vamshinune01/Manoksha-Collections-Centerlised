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

## Email: Brevo (chosen 2 Oct 2026)

Brevo is used through its SMTP relay with the existing SMTP adapter (no extra code). Free plan: 300 emails/day.

1. Owner creates the Brevo account, adds a **sender** (Senders, Domains & Dedicated IPs → Senders) and verifies it. With a own
   domain, also authenticate the domain (DKIM + DMARC DNS records) — much better inbox delivery than a Gmail sender address.
2. SMTP & API → **SMTP** tab → *Generate a new SMTP key*. Note the **SMTP login** shown there (looks like `xxxx@smtp-brevo.com`).
3. Store the key (never in chat or code):
   ```bash
   read -rs "KEY?Brevo SMTP key: "; printf '%s' "$KEY" | gcloud secrets create manoksha-email-smtp-password --replication-policy=automatic --data-file=-; unset KEY
   ```
4. API settings: `Integrations__Email__Provider=Smtp`, `Integrations__Email__Smtp__Host=smtp-relay.brevo.com`,
   `…__Port=587`, `…__UseTls=true`, `…__Username=<SMTP login>`, `…__FromAddress=<verified sender>`,
   `…__FromName=Manoksha Collections`, secret `Integrations__Email__Smtp__Password=manoksha-email-smtp-password:latest`.
5. Admin → Notifications → Email log → **Send me a test email**.
