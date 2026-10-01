terraform {
  required_version = ">= 1.6"
  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 6.0"
    }
  }
  # State lives in a GCS bucket per environment (create it once, versioning on):
  #   terraform init -backend-config="bucket=<project>-tfstate" -backend-config="prefix=manoksha/<env>"
  backend "gcs" {}
}

provider "google" {
  project = var.project_id
  region  = var.region
}
