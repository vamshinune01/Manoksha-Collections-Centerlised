#!/usr/bin/env bash
# Logical backup of the Manoksha database to the private bucket (gs://<private-bucket>/backups/YYYY/MM/<timestamp>.dump).
# Uses pg_dump 16 from the postgres:16-alpine image, so nothing needs to be installed. The connection string is read from Secret
# Manager into memory only; the password is never printed or written to disk.
#
#   PROJECT=manokshacenterlised BUCKET=manokshacenterlised-private infra/scripts/backup-db.sh
#
# Restore (into an EMPTY database — see docs/runbooks/database-backup-restore.md):
#   gcloud storage cp gs://$BUCKET/backups/.../<file>.dump . && pg_restore --no-owner --no-privileges -d "<target>" <file>.dump
set -euo pipefail
: "${PROJECT:?set PROJECT}" "${BUCKET:?set BUCKET}"
SECRET="${SECRET:-manoksha-db-connection}"
export CLOUDSDK_CORE_PROJECT="$PROJECT" CLOUDSDK_BILLING_QUOTA_PROJECT="$PROJECT"

conn="$(gcloud secrets versions access latest --secret="$SECRET")"
# ADO.NET "Host=…;Port=…;Database=…;Username=…;Password=…" → libpq environment variables.
eval "$(CONN="$conn" python3 - <<'PY'
import os, shlex
parts = dict(p.split("=", 1) for p in os.environ["CONN"].split(";") if "=" in p)
get = lambda *keys: next((parts[k] for k in parts if k.lower().replace(" ", "") in keys), "")
for var, keys in [("PGHOST", ("host", "server")), ("PGPORT", ("port",)), ("PGDATABASE", ("database",)), ("PGUSER", ("username", "userid", "user")), ("PGPASSWORD", ("password",))]:
    print(f"export {var}={shlex.quote(get(*keys))}")
print("export PGSSLMODE=require")
PY
)"
unset conn

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
file="$(mktemp -d)/manoksha-$stamp.dump"
docker run --rm -e PGHOST -e PGPORT -e PGDATABASE -e PGUSER -e PGPASSWORD -e PGSSLMODE -v "$(dirname "$file")":/out postgres:16-alpine \
  pg_dump --format=custom --no-owner --no-privileges --file="/out/$(basename "$file")"
unset PGPASSWORD

dest="gs://$BUCKET/backups/$(date -u +%Y/%m)/$(basename "$file")"
gcloud storage cp "$file" "$dest"
rm -f "$file"
echo "Backup written to $dest ($(gcloud storage ls -l "$dest" | awk 'NR==1{print $1}') bytes)"
