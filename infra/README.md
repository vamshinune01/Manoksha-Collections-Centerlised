# infra

- `docker-compose.local.yml` — local PostgreSQL 16 (port 5433) and optional Mailpit (SMTP 1025, UI 8025).
- `sql/database-roles.sql` — Cloud SQL role model (migrator / app / readonly) and append-only grants.
- `terraform/` — GCP projects, Cloud Run (api, worker, admin-web, customer-web, migrate job), Cloud SQL, Secret Manager,
  Cloud Storage, load balancer + Cloud Armor. Delivered in Phase 10 (design §20).

Container images: `manoksha-backend/Dockerfile` (API by default, `--build-arg APP=Worker` for the Worker) and
`manoksha-admin-web/Dockerfile` (build from the repository root).

## Staging (current, low-cost) — project `manokshacenterlised`, region asia-south1

| Piece | Setup |
|---|---|
| Database | Supabase PostgreSQL (free tier), **Session pooler** host, port 5432 (not the transaction pooler; the direct host is IPv6-only). Connection string in Secret Manager `manoksha-db-connection`. Migrations run on API start. |
| API | Cloud Run `manoksha-api` (image `asia-south1-docker.pkg.dev/manokshacenterlised/manoksha/api`), `ASPNETCORE_ENVIRONMENT=Staging`, `Jobs__InProcess=true` (no separate Worker), min 0 / max 1 instance, 1 GiB, 300 s timeout (video optimization). |
| Web | Cloud Run `manoksha-admin-web`, `manoksha-customer-web` (min 0 / max 1), `MANOKSHA_API_URL` = API URL. |
| Media & files | GCS `manokshacenterlised-private` (originals + deposit proofs, public access prevented, CORS PUT from the admin site) and `manokshacenterlised-media` (public read, optimized renditions only). |
| Secrets | `manoksha-db-connection`, `manoksha-auth-signing-key`, `manoksha-auth-data-key`, `manoksha-auth-otp-pepper`, `manoksha-payments-sim-secret` — readable only by `manoksha-run`. |
| Identity | Service account `manoksha-run`: object admin on the two buckets, secret accessor on the secrets, token creator on itself (signed upload URLs via IAM, no key files). |

Staging still uses the development stand-ins (fake SMS — OTP codes in Cloud Logging, logging email, UPI simulator).
Images are built locally with `docker buildx build --platform linux/amd64` and pushed to Artifact Registry (no Cloud Build).
Moving to Cloud SQL later = new connection string (Cloud SQL socket or TCP) + `--add-cloudsql-instances`; no code change.
