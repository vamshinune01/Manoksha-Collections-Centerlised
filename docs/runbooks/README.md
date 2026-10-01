# Runbooks

| Runbook | Use it when |
|---|---|
| [Deploy and roll back](deploy-and-rollback.md) | Releasing a new version, or undoing a bad one |
| [Incident response](incident-response.md) | Something is down, slow, or wrong for customers |
| [Payment reconciliation](payment-reconciliation.md) | Money was received but could not be applied (CRITICAL alert) |
| [Database backup and restore](database-backup-restore.md) | Taking a backup, restoring, or rehearsing a restore |
| [Secret rotation](secret-rotation.md) | A secret leaked, a staff member with access left, or on schedule |
| [Connecting real providers](provider-onboarding.md) | The Owner has chosen the SMS, email or UPI provider |
| [Go-live checklist](go-live-checklist.md) | Before the first real customer |

Common settings for every `gcloud` command below:

```bash
export CLOUDSDK_CORE_PROJECT=<project> CLOUDSDK_BILLING_QUOTA_PROJECT=<project>
REG=asia-south1-docker.pkg.dev/<project>/manoksha; R=asia-south1
```
