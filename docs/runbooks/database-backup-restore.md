# Database backup and restore

**Current hosting:** Supabase PostgreSQL (staging, free tier). The free tier has **no automatic backups** and pauses after a week of
inactivity, so it is not suitable for production as is (see go-live-checklist.md: choose Supabase Pro with daily backups and
point-in-time recovery, or Cloud SQL with automated backups + PITR).

## Logical backup (any host, any time)

```bash
PROJECT=<project> BUCKET=<private-bucket> infra/scripts/backup-db.sh
```
Writes a compressed `pg_dump` to `gs://<private-bucket>/backups/YYYY/MM/`. The connection string is read from Secret Manager and
never printed. Run it before every production migration and at least daily until the host's automatic backups are on. Keep 30
days (bucket lifecycle rule) — backups contain personal data: the bucket is private and access is limited to the runtime account
and the Owner.

## Restore (rehearse once before go-live)

1. Create an **empty** database (a new Supabase project or Cloud SQL database).
2. `gcloud storage cp gs://<bucket>/backups/<yyyy>/<mm>/<file>.dump .`
3. `pg_restore --no-owner --no-privileges --dbname "<target connection URI>" <file>.dump`
   (with Docker: `docker run --rm -v "$PWD":/b postgres:16-alpine pg_restore ... /b/<file>.dump`).
4. Point a **staging** API at it (new secret version), check `/health/ready`, sign in, open a recent order, the wallet ledger and
   the audit log. Only then switch production's connection string and redeploy.

Append-only history (audit log, wallet ledger, stock movements) is restored as-is; its database triggers are part of the schema.
