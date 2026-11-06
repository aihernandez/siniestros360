variable "blob_sas_expiry_utc" {
  type        = string
  description = "Explicit SAS expiry in UTC; rotate this value before expiration. Current application requires a container SAS."
}

variable "blob_sas_start_utc" {
  type        = string
  description = "Explicit UTC start for SAS validity. Allow operator clock skew; no implicit timestamp drift."
}

variable "policy_provider_endpoint" {
  type        = string
  description = "Real policy provider URL. Empty retains the demo provider; live publication requires an explicit application-readiness acknowledgement."
  default     = ""
}

variable "postgresql_ssl_root_certificate" {
  type        = string
  description = "Trusted PostgreSQL CA PEM path, or null to use the client trust store. Never disable TLS verification."
  default     = null
}

variable "region_key" {
  type        = string
  description = "Deploy exactly one region per independent state and private runner."
}

variable "state" {
  type        = object({ resource_group_name = string, storage_account_name = string, container_name = string, platform_key = string })
  description = "Azure AD authenticated remote state location. All operators with access can read its sensitive contents."
}

variable "subscription_id" {
  type        = string
  description = "Azure subscription ID; authenticate through Azure CLI or workload federation."
}

variable "tls_pfx_base64" {
  type        = string
  description = "Base64 PFX with private key and complete chain, covering API/customer/adjuster/tower hostnames."
  sensitive   = true
}

variable "tls_pfx_password" {
  type        = string
  description = "PFX import password."
  sensitive   = true
}
