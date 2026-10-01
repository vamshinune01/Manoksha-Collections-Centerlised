output "api_url" {
  value = google_cloud_run_v2_service.api.uri
}

output "admin_url" {
  value = google_cloud_run_v2_service.web["admin"].uri
}

output "customer_url" {
  value = google_cloud_run_v2_service.web["customer"].uri
}

output "runtime_service_account" {
  value = google_service_account.run.email
}
