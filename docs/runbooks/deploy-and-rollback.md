# Deploy and roll back

Every image is tagged with the git commit (`git rev-parse --short HEAD`). Cloud Run keeps every revision, so rolling back is
moving traffic, not rebuilding.

## Release

1. `main` is green in CI (backend tests incl. the authorization sweep, web lint/typecheck/build, vulnerability scan).
2. Build and push (from the repo root, Apple Silicon needs `--platform linux/amd64`):
   ```bash
   TAG=$(git rev-parse --short HEAD)
   docker buildx build --platform linux/amd64 -t $REG/api:$TAG --push manoksha-backend
   docker buildx build --platform linux/amd64 --build-arg APP=Worker -t $REG/worker:$TAG --push manoksha-backend   # production
   for app in admin customer; do docker buildx build --platform linux/amd64 -f manoksha-$app-web/Dockerfile -t $REG/$app-web:$TAG --push .; done
   ```
3. **Database migrations.** Staging: the API applies them on start. Production: run the job first, and only continue if it succeeds:
   ```bash
   gcloud run jobs update manoksha-migrate --region=$R --image=$REG/api:$TAG && gcloud run jobs execute manoksha-migrate --region=$R --wait
   ```
   Migrations are additive (new tables/columns, nullable or defaulted) so the previous version keeps working during the rollout.
   A migration that removes or renames something is split over two releases.
4. Deploy, API first (the web apps call it):
   ```bash
   gcloud run deploy manoksha-api --region=$R --image=$REG/api:$TAG
   gcloud run deploy manoksha-worker --region=$R --image=$REG/worker:$TAG                         # production
   gcloud run deploy manoksha-admin-web --region=$R --image=$REG/admin-web:$TAG
   gcloud run deploy manoksha-customer-web --region=$R --image=$REG/customer-web:$TAG
   ```
5. Smoke test (2 minutes): `curl -fsS https://<api>/health/ready`; open the shop home page and a product; sign in to the admin
   site and open the Dashboard and Exception Center; on the POS phone, open Today's sales. Watch Cloud Logging for errors for
   10 minutes.

For a risky change, deploy with `--no-traffic --tag=canary`, test the canary URL, then
`gcloud run services update-traffic manoksha-api --region=$R --to-latest`.

## Roll back

```bash
gcloud run revisions list --service=manoksha-api --region=$R --limit=5
gcloud run services update-traffic manoksha-api --region=$R --to-revisions=<previous-revision>=100
```
Do the same for the web apps/worker if they changed. Rolling back code never rolls back the database; because migrations are
additive, the previous revision runs on the newer schema. If a migration itself is wrong, fix forward with a new migration — do
not hand-edit tables (audit, ledger and stock history are append-only by design).

## After a rollback
Write down what happened in the incident log (see incident-response.md) and add a test that would have caught it.
