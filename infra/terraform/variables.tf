variable "project_id" {
  type = string
}

variable "region" {
  type    = string
  default = "asia-south1"
}

variable "environment" {
  description = "Staging or Production (ASP.NET Core environment name)."
  type        = string
  validation {
    condition     = contains(["Staging", "Production"], var.environment)
    error_message = "environment must be Staging or Production."
  }
}

variable "image_tag" {
  description = "Image tag (git short SHA) deployed to every service."
  type        = string
}

variable "private_bucket" {
  type = string
}

variable "media_bucket" {
  type = string
}

variable "api_min_instances" {
  type    = number
  default = 0
}

variable "web_min_instances" {
  type    = number
  default = 0
}

variable "max_instances" {
  type    = number
  default = 1
}

variable "run_worker" {
  description = "Production: a separate always-on Worker (jobs). Staging: jobs run inside the API (cheaper)."
  type        = bool
  default     = false
}

variable "integrations" {
  description = "Provider per integration. Fake/Logging/Simulator are refused by the API in Production."
  type = object({
    sms      = string
    email    = string
    payments = string
  })
}

variable "admin_url" {
  description = "Public admin site URL (custom domain in production), used for email links and storage CORS."
  type        = string
}

variable "customer_url" {
  description = "Public shop URL (custom domain in production), used for email links and payment return."
  type        = string
}

variable "alert_email" {
  description = "Where uptime and CRITICAL alerts are sent (the Owner)."
  type        = string
}

variable "extra_api_env" {
  description = "Additional non-secret API settings (e.g. provider endpoints)."
  type        = map(string)
  default     = {}
}

variable "extra_api_secrets" {
  description = "Additional API settings read from Secret Manager: { ENV_NAME = secret_id }."
  type        = map(string)
  default     = {}
}
