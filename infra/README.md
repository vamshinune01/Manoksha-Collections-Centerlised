# infra

- `docker-compose.local.yml` — local PostgreSQL 16 (port 5433) and optional Mailpit (SMTP 1025, UI 8025).
- `sql/database-roles.sql` — Cloud SQL role model (migrator / app / readonly) and append-only grants.
- `terraform/` — GCP projects, Cloud Run (api, worker, admin-web, customer-web, migrate job), Cloud SQL, Secret Manager,
  Cloud Storage, load balancer + Cloud Armor. Delivered in Phase 10 (design §20).

Container images: `manoksha-backend/Dockerfile` (API by default, `--build-arg APP=Worker` for the Worker) and
`manoksha-admin-web/Dockerfile` (build from the repository root).
