# Production (fill in at go-live; see docs/runbooks/go-live-checklist.md). Real providers are required — the API refuses the
# development stand-ins in Production. Provider settings/secrets go in extra_api_env / extra_api_secrets once chosen.
project_id        = "REPLACE_WITH_PRODUCTION_PROJECT"
environment       = "Production"
image_tag         = "REPLACE_WITH_GIT_SHA"
private_bucket    = "REPLACE-private"
media_bucket      = "REPLACE-media"
api_min_instances = 1 # no cold starts at the counter
web_min_instances = 0
max_instances     = 3
run_worker        = true
integrations      = { sms = "REPLACE_SMS_PROVIDER", email = "REPLACE_EMAIL_PROVIDER", payments = "REPLACE_UPI_GATEWAY" }
admin_url         = "https://admin.REPLACE_DOMAIN"
customer_url      = "https://REPLACE_DOMAIN"
alert_email       = "REPLACE_WITH_OWNER_EMAIL"
