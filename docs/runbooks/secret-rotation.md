# Secret rotation

All secrets live in Secret Manager and are read only by the `manoksha-run` service account. Add a new version, then redeploy the
services that use it (Cloud Run reads secrets at start):

```bash
read -rs "VALUE?New value: "; printf '%s' "$VALUE" | gcloud secrets versions add <secret> --data-file=-; unset VALUE
gcloud run services update manoksha-api --region=$R   # new revision picks up "latest"
```
Disable the old version after the new revision is healthy: `gcloud secrets versions disable <n> --secret=<secret>`.

| Secret | Generate with | Effect of rotating |
|---|---|---|
| `manoksha-db-connection` | Change the DB password at the host first | None if both are changed together |
| `manoksha-auth-signing-key` | `openssl ecparam -name prime256v1 -genkey -noout \| openssl pkcs8 -topk8 -nocrypt` | Everyone is signed out (access tokens stop validating) |
| `manoksha-auth-otp-pepper` | `openssl rand -base64 32` | Codes already sent stop working; users request a new one |
| `manoksha-auth-data-key` | `openssl rand -base64 32` | **Encrypts staff authenticator secrets.** Rotating makes existing MFA enrolments unreadable: only rotate if leaked, then every staff member re-enrols MFA |
| `manoksha-proxy-key` | `openssl rand -base64 48` | Update API **and both web apps** in the same window; until all three match, rate limits/audit see the web server's IP |
| `manoksha-owner-setup-code` | `openssl rand -base64 24` | Only used before the first Owner exists |
| Provider keys (SMS, email, UPI) | Provider dashboard | Rotate at the provider, add the version, redeploy |

Rotate immediately when someone with access leaves or a secret may have been exposed; otherwise yearly. Never paste secret values
into chat, tickets or the repository.
