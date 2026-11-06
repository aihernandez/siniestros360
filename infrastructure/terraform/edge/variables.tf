variable "application_readiness_confirmed" {
  type        = bool
  description = "Explicit confirmation that images, migrations, credentials and current demo seeding/session risks have been resolved before Internet traffic."
  default     = false
}

variable "base_domain" {
  type        = string
  description = "Dedicated delegated DNS zone, e.g. claims.example.com; certificate must cover api/insured/adjuster/tower.<zone>."
  validation {
    condition     = can(regex("^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z]{2,63}$", var.base_domain))
    error_message = "Supply a lowercase DNS zone without a scheme, path, port or wildcard."
  }
}

variable "data_keys" {
  type        = map(string)
  description = "Regional-data state key for every geography entry."
}

variable "enable_public_traffic" {
  type        = bool
  description = "Open regional WAF publication and Traffic Manager endpoints only after readiness/fencing confirmations."
  default     = false
}

variable "geographies" {
  type        = map(set(string))
  description = "Traffic Manager geographic codes per regional key; Mexico is a more-specific override of North America."
  default = {
    centralus     = ["GEO-NA"]
    francecentral = ["GEO-EU"]
    mexicocentral = ["MX"]
    chilecentral  = ["GEO-SA"]
  }
}

variable "name" {
  type        = string
  description = "Unique global resource and Traffic Manager DNS prefix."
  default     = "s360-reference"
}

variable "ownership_routing_confirmed" {
  type        = bool
  description = "Confirm single-writer home-region routing/fencing and data-residency decisions are implemented before four-region traffic."
  default     = false
}

variable "recovery_ready_regions" {
  type        = set(string)
  description = "Regions whose configured backups have verified data/message/object readiness and writer fencing."
  default     = []
}

variable "recovery_targets" {
  type        = map(object({ region = string, fqdn = string, priority = number }))
  description = "Optional separately prepared backup endpoints; no replica or automatic data failover is implied."
  default     = {}
}

variable "state" {
  type        = object({ resource_group_name = string, storage_account_name = string, container_name = string, platform_key = string })
  description = "Azure AD authenticated remote state location. All operators with access can read its sensitive contents."
}

variable "subscription_id" {
  type        = string
  description = "Azure subscription ID; authenticate through Azure CLI or workload federation."
}

variable "world_fallback_region" {
  type        = string
  description = "Optional regional key for WORLD mapping; null leaves uncovered geographies without a geographic endpoint."
  default     = null
}
