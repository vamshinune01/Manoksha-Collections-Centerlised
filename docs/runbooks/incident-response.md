# Incident response

## Severity

| Level | Examples | Response |
|---|---|---|
| **SEV1** | Shop or POS cannot sell; payments taken but orders not created; data exposed | Act now, Owner informed immediately |
| **SEV2** | One feature broken (emails, reports, one branch's printer); slow pages | Same day |
| **SEV3** | Cosmetic, a single user affected | Next release |

## First 10 minutes

1. **Is it down?** `curl -s -o /dev/null -w "%{http_code}" https://<api>/health/ready` (200 = API and database fine; 503 = database
   unreachable). Check the admin and shop home pages.
2. **What changed?** Last deploy time: `gcloud run revisions list --service=manoksha-api --region=$R --limit=3`. If the problem
   started with a deploy, **roll back first, investigate after** (deploy-and-rollback.md).
3. **Errors:** Cloud Logging → `resource.labels.service_name="manoksha-api" severity>=ERROR`. Every error has a `correlationId`
   that users also see in error messages — search for it.
4. **Business safety:** open the admin **Exception Center** — payments to reconcile, fulfillment exceptions, failed emails.

## Known situations

| Symptom | Likely cause | What to do |
|---|---|---|
| `/health/ready` 503 | Database unreachable (Supabase paused/maintenance, password changed) | Check the Supabase dashboard; restore the connection string secret; redeploy the API to pick up a new secret version |
| Customers get "Too many requests" | Rate limit; or web apps missing `MANOKSHA_PROXY_KEY` so everyone shares the web server's IP | Check the web services' env; limits are `RateLimiting:*` settings on the API |
| Online payments stay "pending" | Provider webhook not arriving | The payment poller recovers them automatically (every 30 s); check provider status; never mark paid by hand — use reconciliation |
| CRITICAL "payment reconciliation required" | Paid but stock gone / amount mismatch | payment-reconciliation.md |
| Emails not arriving | Provider outage or bad credentials | Exception Center → Failed notifications → Retry once fixed; nothing else is affected |
| POS says "Not connected" | Shop internet down | No sale is recorded until connected (SPEC §23); the bill stays on the phone — tap Pay again when back |
| Staff locked out | 5 wrong passwords/PINs → 15 min lock (sensitive alert raised) | Wait 15 minutes or reset the password from Users; check the alert for suspicious IPs |
| Wallet integrity alert | Ledger and balance disagree (should never happen) | Freeze the reseller's account, do not adjust by hand, escalate to the developer with the alert details |

## Afterwards
Record: start/end time, impact (orders, customers), cause, fix, and a follow-up (test, alert or runbook change).
