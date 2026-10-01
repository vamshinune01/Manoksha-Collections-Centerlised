# Terraform — Manoksha on Google Cloud

Describes everything the platform runs on: Artifact Registry, the runtime service account (least privilege), Secret Manager
secrets (containers only — values are added by a person), the two Cloud Storage buckets, Cloud Run services (API, admin web,
customer web, and in production a Worker plus a migration job), and free-tier monitoring (uptime checks, 5xx and CRITICAL-log
alerts emailed to the Owner). The database is **not** managed here: it is a connection string in `manoksha-db-connection`
(Supabase today, Cloud SQL possible later — ADR-001 §28).

```bash
cd infra/terraform
terraform init -backend-config="bucket=<project>-tfstate" -backend-config="prefix=manoksha/staging"
terraform plan  -var-file=envs/staging.tfvars -var image_tag=$(git rev-parse --short HEAD)
terraform apply -var-file=envs/staging.tfvars -var image_tag=$(git rev-parse --short HEAD)
```

**Staging was created by hand before this code existed.** Before the first `apply` there, import the existing resources so
Terraform adopts them instead of recreating them (then `plan` must show only intended changes):

```bash
P=manokshacenterlised; R=asia-south1
terraform import -var-file=envs/staging.tfvars google_artifact_registry_repository.images projects/$P/locations/$R/repositories/manoksha
terraform import -var-file=envs/staging.tfvars google_service_account.run projects/$P/serviceAccounts/manoksha-run@$P.iam.gserviceaccount.com
terraform import -var-file=envs/staging.tfvars google_storage_bucket.private $P-private
terraform import -var-file=envs/staging.tfvars google_storage_bucket.media $P-media
for s in manoksha-db-connection manoksha-auth-signing-key manoksha-auth-data-key manoksha-auth-otp-pepper manoksha-owner-setup-code manoksha-proxy-key manoksha-payments-sim-secret; do
  terraform import -var-file=envs/staging.tfvars "google_secret_manager_secret.secrets[\"$s\"]" projects/$P/secrets/$s
done
terraform import -var-file=envs/staging.tfvars google_cloud_run_v2_service.api projects/$P/locations/$R/services/manoksha-api
terraform import -var-file=envs/staging.tfvars 'google_cloud_run_v2_service.web["admin"]' projects/$P/locations/$R/services/manoksha-admin-web
terraform import -var-file=envs/staging.tfvars 'google_cloud_run_v2_service.web["customer"]' projects/$P/locations/$R/services/manoksha-customer-web
```

Cost notes: everything scales to zero in staging; uptime checks, alert policies and log-based metrics are within Google's free
allowance at this size. Cloud Armor and a load balancer are intentionally not used (about USD 20+/month); the API has its own
per-user/per-IP rate limits. Add them later if traffic or attacks justify it.
