variable "admin_group_object_ids" {
  type        = set(string)
  description = "Existing Entra groups with AKS cluster-admin access."
  validation {
    condition     = length(var.admin_group_object_ids) > 0
    error_message = "Supply at least one existing AKS administrator group."
  }
}

variable "alert_email" {
  type        = string
  description = "Operations email receiving Azure Monitor alerts; no email is sent during local validation."
}

variable "environment" {
  type        = string
  description = "Environment naming segment."
  default     = "reference"
  validation {
    condition     = can(regex("^[a-z][a-z0-9]{1,11}$", var.environment))
    error_message = "Use 2-12 lowercase letters/digits for resource-compatible environment names."
  }
}

variable "kubernetes_version" {
  type        = string
  description = "AKS version; null selects the regional platform default. Confirm support before a real plan."
  default     = null
}

variable "name_prefix" {
  type        = string
  description = "Short lowercase name prefix, unique across subscriptions as needed."
  default     = "s360"
  validation {
    condition     = can(regex("^[a-z][a-z0-9]{2,7}$", var.name_prefix))
    error_message = "Use 3-8 lowercase letters/digits starting with a letter."
  }
}

variable "operator_object_id" {
  type        = string
  description = "Deployment principal Entra object ID; gets AKS RBAC Cluster Admin and secret provisioning permissions."
}

variable "regions" {
  type        = map(object({ location = string, vnet_cidr = string, zones = list(string), postgres_zone_redundant = bool }))
  description = "Independent deployment stamps. Addresses must be non-overlapping /16 networks."
  default = {
    centralus     = { location = "centralus", vnet_cidr = "10.40.0.0/16", zones = ["1", "2", "3"], postgres_zone_redundant = true }
    francecentral = { location = "francecentral", vnet_cidr = "10.41.0.0/16", zones = ["1", "2", "3"], postgres_zone_redundant = true }
    mexicocentral = { location = "mexicocentral", vnet_cidr = "10.42.0.0/16", zones = ["1", "2", "3"], postgres_zone_redundant = false }
    chilecentral  = { location = "chilecentral", vnet_cidr = "10.43.0.0/16", zones = ["1", "2", "3"], postgres_zone_redundant = true }
  }
  validation {
    condition     = length(var.regions) > 0 && length(distinct([for r in values(var.regions) : r.vnet_cidr])) == length(var.regions) && alltrue([for r in values(var.regions) : can(cidrnetmask(r.vnet_cidr)) && endswith(r.vnet_cidr, "/16") && try(cidrhost(r.vnet_cidr, 0) == split("/", r.vnet_cidr)[0], false) && length(r.zones) >= 1])
    error_message = "Use distinct /16 IPv4 regional networks and at least one availability zone."
  }
}

variable "subscription_id" {
  type        = string
  description = "Azure subscription ID; authenticate through Azure CLI or workload federation."
}

variable "tenant_id" {
  type        = string
  description = "Existing workforce/resource tenant GUID."
}
