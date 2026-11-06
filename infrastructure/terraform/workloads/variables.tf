variable "base_domain" {
  type        = string
  description = "Same delegated base DNS zone used by edge."
  validation {
    condition     = can(regex("^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z]{2,63}$", var.base_domain))
    error_message = "Supply a lowercase DNS zone without a scheme, path, port or wildcard."
  }
}

variable "collector_image" {
  type        = string
  description = "Pinned OpenTelemetry contrib image including the azuremonitor exporter."
}

variable "data_state_key" {
  type        = string
  description = "Regional-data remote state key for this region."
}

variable "frontend_images" {
  type        = map(string)
  description = "Built SPA images keyed insured-app, adjuster-app, control-tower."
}

variable "migration_images" {
  type        = map(string)
  description = "Nine EF migration bundle images keyed by service. Build with infrastructure/containers/migrations.Dockerfile."
  validation {
    condition     = length(var.migration_images) == 9 && alltrue([for key in ["identity", "claims", "adjusters", "location", "dispatch", "policy", "sla", "documents", "operations"] : contains(keys(var.migration_images), key)])
    error_message = "Supply an explicit versioned migration image for each of the nine services."
  }
}

variable "namespace" {
  type        = string
  description = "Namespace fixed by the platform's workload federation subjects."
  default     = "siniestros360"
  validation {
    condition     = var.namespace == "siniestros360"
    error_message = "The namespace must match platform federated credentials: siniestros360."
  }
}

variable "proxy_image" {
  type        = string
  description = "Pinned nginxinc/nginx-unprivileged-compatible image supporting port 8080."
}

variable "region_key" {
  type        = string
  description = "One regional deployment per state; run from that region's private network."
}

variable "release_id" {
  type        = string
  description = "Immutable release identifier/digest used to version migration jobs and restart workloads."
}

variable "service_images" {
  type        = map(string)
  description = "Versioned backend images keyed by nine services plus gateway."
}

variable "state" {
  type        = object({ resource_group_name = string, storage_account_name = string, container_name = string, platform_key = string })
  description = "Azure AD authenticated remote state location. All operators with access can read its sensitive contents."
}

variable "subscription_id" {
  type        = string
  description = "Azure subscription ID; authenticate through Azure CLI or workload federation."
}
variable "environment" {
  type        = string
  description = "Environment label used by regional workload telemetry."
  default     = "reference"
  validation {
    condition     = can(regex("^[a-z][a-z0-9]{1,11}$", var.environment))
    error_message = "Use 2-12 lowercase letters/digits for the deployment environment."
  }
}
