# Low-cost staging (current): Supabase PostgreSQL, scale to zero, jobs inside the API, development stand-ins for providers.
project_id        = "manokshacenterlised"
environment       = "Staging"
image_tag         = "REPLACE_WITH_GIT_SHA"
private_bucket    = "manokshacenterlised-private"
media_bucket      = "manokshacenterlised-media"
api_min_instances = 0
web_min_instances = 0
max_instances     = 1
run_worker        = false
integrations      = { sms = "Fake", email = "Logging", payments = "Simulator" }
admin_url         = "https://manoksha-admin-web-939136478134.asia-south1.run.app"
customer_url      = "https://manoksha-customer-web-939136478134.asia-south1.run.app"
alert_email       = "REPLACE_WITH_OWNER_EMAIL"
