locals {
  registry = "${var.region}-docker.pkg.dev/${var.project_id}/manoksha"
  services = toset(["run.googleapis.com", "artifactregistry.googleapis.com", "secretmanager.googleapis.com", "iamcredentials.googleapis.com",
  "monitoring.googleapis.com", "logging.googleapis.com"])

  # Secrets: Terraform creates the containers only; values are added by a person (never in code or state):
  #   printf '%s' "$VALUE" | gcloud secrets versions add <id> --data-file=-
  secrets = toset(["manoksha-db-connection", "manoksha-auth-signing-key", "manoksha-auth-data-key", "manoksha-auth-otp-pepper",
  "manoksha-owner-setup-code", "manoksha-proxy-key", "manoksha-payments-sim-secret"])

  api_env = merge({
    ASPNETCORE_ENVIRONMENT                     = var.environment
    Database__MigrateOnStartup                 = var.environment == "Production" ? "false" : "true"
    Jobs__InProcess                            = var.run_worker ? "false" : "true"
    Integrations__Sms__Provider                = var.integrations.sms
    Integrations__Email__Provider              = var.integrations.email
    Integrations__Storage__Provider            = "Gcs"
    Integrations__Payments__Provider           = var.integrations.payments
    Integrations__Storage__Gcs__PrivateBucket  = var.private_bucket
    Integrations__Storage__Gcs__PublicBucket   = var.media_bucket
    Integrations__Payments__Simulator__CheckoutBaseUrl = var.customer_url
    Notifications__AdminWebUrl                 = var.admin_url
    Notifications__CustomerWebUrl              = var.customer_url
  }, var.extra_api_env)

  api_secrets = merge({
    ConnectionStrings__Manoksha = "manoksha-db-connection"
    Auth__SigningKeyPem         = "manoksha-auth-signing-key"
    Auth__DataEncryptionKey     = "manoksha-auth-data-key"
    Auth__Otp__Pepper           = "manoksha-auth-otp-pepper"
    Setup__OwnerSetupCode       = "manoksha-owner-setup-code"
    Security__ProxyKey          = "manoksha-proxy-key"
    }, var.integrations.payments == "Simulator" ? { Integrations__Payments__Simulator__WebhookSecret = "manoksha-payments-sim-secret" } : {},
  var.extra_api_secrets)
}

resource "google_project_service" "apis" {
  for_each           = local.services
  service            = each.value
  disable_on_destroy = false
}

resource "google_artifact_registry_repository" "images" {
  repository_id = "manoksha"
  location      = var.region
  format        = "DOCKER"
  cleanup_policies {
    id     = "keep-recent"
    action = "KEEP"
    most_recent_versions {
      keep_count = 10
    }
  }
  cleanup_policies {
    id     = "delete-old"
    action = "DELETE"
    condition {
      older_than = "2592000s" # 30 days
    }
  }
  depends_on = [google_project_service.apis]
}

# ---- Identity: one runtime service account, least privilege ----

resource "google_service_account" "run" {
  account_id   = "manoksha-run"
  display_name = "Manoksha Cloud Run runtime"
}

resource "google_service_account_iam_member" "self_sign" {
  # Signed upload/read URLs via the IAM signBlob API — no key files.
  service_account_id = google_service_account.run.name
  role               = "roles/iam.serviceAccountTokenCreator"
  member             = "serviceAccount:${google_service_account.run.email}"
}

resource "google_secret_manager_secret" "secrets" {
  for_each  = local.secrets
  secret_id = each.value
  replication {
    auto {}
  }
  depends_on = [google_project_service.apis]
}

resource "google_secret_manager_secret_iam_member" "run_reads" {
  for_each  = google_secret_manager_secret.secrets
  secret_id = each.value.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.run.email}"
}

# ---- Storage: private originals/proofs, public optimized media ----

resource "google_storage_bucket" "private" {
  name                        = var.private_bucket
  location                    = var.region
  uniform_bucket_level_access = true
  public_access_prevention    = "enforced"
  versioning {
    enabled = var.environment == "Production"
  }
  cors {
    origin          = [var.admin_url]
    method          = ["PUT"]
    response_header = ["Content-Type"]
    max_age_seconds = 3600
  }
}

resource "google_storage_bucket" "media" {
  name                        = var.media_bucket
  location                    = var.region
  uniform_bucket_level_access = true
  cors {
    origin          = ["*"]
    method          = ["GET", "HEAD"]
    max_age_seconds = 3600
  }
}

resource "google_storage_bucket_iam_member" "media_public" {
  bucket = google_storage_bucket.media.name
  role   = "roles/storage.objectViewer"
  member = "allUsers"
}

resource "google_storage_bucket_iam_member" "run_objects" {
  for_each = toset([google_storage_bucket.private.name, google_storage_bucket.media.name])
  bucket   = each.value
  role     = "roles/storage.objectAdmin"
  member   = "serviceAccount:${google_service_account.run.email}"
}

# ---- Cloud Run ----

resource "google_cloud_run_v2_service" "api" {
  name     = "manoksha-api"
  location = var.region
  ingress  = "INGRESS_TRAFFIC_ALL"
  template {
    service_account                  = google_service_account.run.email
    timeout                          = "300s"
    max_instance_request_concurrency = 80
    scaling {
      min_instance_count = var.api_min_instances
      max_instance_count = var.max_instances
    }
    containers {
      image = "${local.registry}/api:${var.image_tag}"
      resources {
        limits            = { cpu = "1", memory = "1Gi" }
        cpu_idle          = true
        startup_cpu_boost = true
      }
      dynamic "env" {
        for_each = local.api_env
        content {
          name  = env.key
          value = env.value
        }
      }
      dynamic "env" {
        for_each = local.api_secrets
        content {
          name = env.key
          value_source {
            secret_key_ref {
              secret  = env.value
              version = "latest"
            }
          }
        }
      }
      startup_probe {
        http_get {
          path = "/health/live"
        }
        initial_delay_seconds = 5
        period_seconds        = 5
        failure_threshold     = 24
      }
    }
  }
  depends_on = [google_secret_manager_secret_iam_member.run_reads]
}

resource "google_cloud_run_v2_service" "web" {
  for_each = { admin = "admin-web", customer = "customer-web" }
  name     = "manoksha-${each.value}"
  location = var.region
  ingress  = "INGRESS_TRAFFIC_ALL"
  template {
    service_account = google_service_account.run.email
    scaling {
      min_instance_count = var.web_min_instances
      max_instance_count = var.max_instances
    }
    containers {
      image = "${local.registry}/${each.value}:${var.image_tag}"
      resources {
        limits            = { cpu = "1", memory = "512Mi" }
        cpu_idle          = true
        startup_cpu_boost = true
      }
      env {
        name  = "MANOKSHA_API_URL"
        value = google_cloud_run_v2_service.api.uri
      }
      env {
        name = "MANOKSHA_PROXY_KEY"
        value_source {
          secret_key_ref {
            secret  = "manoksha-proxy-key"
            version = "latest"
          }
        }
      }
    }
  }
}

# Production: jobs run in an always-on Worker (CPU always allocated), and migrations run as a job before each release.
resource "google_cloud_run_v2_service" "worker" {
  count    = var.run_worker ? 1 : 0
  name     = "manoksha-worker"
  location = var.region
  ingress  = "INGRESS_TRAFFIC_INTERNAL_ONLY"
  template {
    service_account = google_service_account.run.email
    scaling {
      min_instance_count = 1
      max_instance_count = 1
    }
    containers {
      image = "${local.registry}/worker:${var.image_tag}"
      resources {
        limits   = { cpu = "1", memory = "512Mi" }
        cpu_idle = false
      }
      dynamic "env" {
        for_each = merge(local.api_env, { Jobs__InProcess = "false" })
        content {
          name  = env.key
          value = env.value
        }
      }
      dynamic "env" {
        for_each = local.api_secrets
        content {
          name = env.key
          value_source {
            secret_key_ref {
              secret  = env.value
              version = "latest"
            }
          }
        }
      }
    }
  }
}

resource "google_cloud_run_v2_job" "migrate" {
  count    = var.environment == "Production" ? 1 : 0
  name     = "manoksha-migrate"
  location = var.region
  template {
    template {
      service_account = google_service_account.run.email
      max_retries     = 0
      containers {
        image = "${local.registry}/api:${var.image_tag}"
        args  = ["migrate"]
        env {
          name  = "ASPNETCORE_ENVIRONMENT"
          value = var.environment
        }
        dynamic "env" {
          for_each = { ConnectionStrings__Manoksha = "manoksha-db-connection" }
          content {
            name = env.key
            value_source {
              secret_key_ref {
                secret  = env.value
                version = "latest"
              }
            }
          }
        }
      }
    }
  }
}

resource "google_cloud_run_v2_service_iam_member" "public" {
  for_each = merge({ api = google_cloud_run_v2_service.api.name }, { for k, s in google_cloud_run_v2_service.web : k => s.name })
  name     = each.value
  location = var.region
  role     = "roles/run.invoker"
  member   = "allUsers"
}
