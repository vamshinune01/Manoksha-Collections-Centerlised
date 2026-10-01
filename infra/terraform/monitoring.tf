# Free-tier monitoring: uptime checks, an email channel and alert policies (no extra paid services).

resource "google_monitoring_notification_channel" "owner" {
  display_name = "Owner email"
  type         = "email"
  labels = {
    email_address = var.alert_email
  }
}

locals {
  uptime_targets = {
    api      = { host = trimprefix(google_cloud_run_v2_service.api.uri, "https://"), path = "/health/ready" }
    admin    = { host = trimprefix(var.admin_url, "https://"), path = "/login" }
    customer = { host = trimprefix(var.customer_url, "https://"), path = "/" }
  }
}

resource "google_monitoring_uptime_check_config" "sites" {
  for_each     = local.uptime_targets
  display_name = "manoksha-${each.key}"
  timeout      = "10s"
  period       = "300s"
  http_check {
    path         = each.value.path
    port         = 443
    use_ssl      = true
    validate_ssl = true
  }
  monitored_resource {
    type = "uptime_url"
    labels = {
      project_id = var.project_id
      host       = each.value.host
    }
  }
}

resource "google_monitoring_alert_policy" "uptime" {
  display_name = "Manoksha site down"
  combiner     = "OR"
  conditions {
    display_name = "Uptime check failing"
    condition_threshold {
      filter          = "metric.type=\"monitoring.googleapis.com/uptime_check/check_passed\" AND resource.type=\"uptime_url\""
      comparison      = "COMPARISON_GT"
      threshold_value = 1
      duration        = "600s"
      aggregations {
        alignment_period     = "300s"
        per_series_aligner   = "ALIGN_NEXT_OLDER"
        cross_series_reducer = "REDUCE_COUNT_FALSE"
        group_by_fields      = ["resource.label.host"]
      }
    }
  }
  notification_channels = [google_monitoring_notification_channel.owner.id]
}

resource "google_logging_metric" "api_5xx" {
  name   = "manoksha_api_5xx"
  filter = "resource.type=\"cloud_run_revision\" AND resource.labels.service_name=\"manoksha-api\" AND httpRequest.status>=500"
  metric_descriptor {
    metric_kind = "DELTA"
    value_type  = "INT64"
  }
}

resource "google_logging_metric" "critical" {
  # Payments that could not be applied, wallet integrity, unhandled exceptions — anything the API logs at CRITICAL.
  name   = "manoksha_critical_logs"
  filter = "resource.type=\"cloud_run_revision\" AND resource.labels.service_name=~\"manoksha-(api|worker)\" AND severity=CRITICAL"
  metric_descriptor {
    metric_kind = "DELTA"
    value_type  = "INT64"
  }
}

resource "google_monitoring_alert_policy" "errors" {
  display_name = "Manoksha API errors"
  combiner     = "OR"
  conditions {
    display_name = "More than 5 server errors in 5 minutes"
    condition_threshold {
      filter          = "metric.type=\"logging.googleapis.com/user/${google_logging_metric.api_5xx.name}\" AND resource.type=\"cloud_run_revision\""
      comparison      = "COMPARISON_GT"
      threshold_value = 5
      duration        = "0s"
      aggregations {
        alignment_period   = "300s"
        per_series_aligner = "ALIGN_SUM"
      }
    }
  }
  conditions {
    display_name = "Any CRITICAL log"
    condition_threshold {
      filter          = "metric.type=\"logging.googleapis.com/user/${google_logging_metric.critical.name}\" AND resource.type=\"cloud_run_revision\""
      comparison      = "COMPARISON_GT"
      threshold_value = 0
      duration        = "0s"
      aggregations {
        alignment_period   = "300s"
        per_series_aligner = "ALIGN_SUM"
      }
    }
  }
  notification_channels = [google_monitoring_notification_channel.owner.id]
}
