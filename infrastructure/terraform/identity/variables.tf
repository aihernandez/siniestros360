variable "customer_redirect_uris" {
  type        = set(string)
  description = "Customer SPA HTTPS redirect URIs. Native Capacitor registrations require separate verified callback URIs."
}

variable "customer_native_redirect_uris" {
  type        = set(string)
  description = "Optional verified customer mobile callbacks; no value is inferred from a signing certificate."
  default     = []
}

variable "workforce_native_redirect_uris" {
  type        = set(string)
  description = "Optional verified adjuster mobile callbacks."
  default     = []
}

variable "customer_tenant_id" {
  type        = string
  description = "Existing or separately provisioned External ID tenant GUID; credentials must have app registration permissions there."
}

variable "workforce_owner_object_ids" {
  type        = set(string)
  description = "Existing object IDs owning app registrations in each tenant; tenant-local object IDs."
  default     = []
}

variable "workforce_redirect_uris" {
  type        = set(string)
  description = "Corporate adjuster/tower SPA HTTPS redirect URIs."
}

variable "workforce_tenant_id" {
  type        = string
  description = "Existing corporate tenant GUID."
}
variable "customer_owner_object_ids" {
  type        = set(string)
  description = "Customer-tenant object IDs owning its registrations."
  default     = []
}
variable "name" {
  type        = string
  description = "Environment-specific registration prefix; keep dev, QA and production applications separate."
  default     = "siniestros360"
}
