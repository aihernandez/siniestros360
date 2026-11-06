variable "admin_group_object_ids" {
  type        = set(string)
  description = "Existing AKS administrator groups."
}

variable "admin_password" {
  type        = string
  description = "PostgreSQL bootstrap password."
  sensitive   = true
}

variable "alert_email" {
  type        = string
  description = "Alert email recipient."
}

variable "kubernetes_version" {
  type        = string
  description = "Regional supported AKS version, or null."
}

variable "location" {
  type        = string
  description = "Azure location."
}

variable "name" {
  type        = string
  description = "Unique regional name."
}

variable "operator_object_id" {
  type        = string
  description = "Deployment principal object ID."
}

variable "postgres_zone_redundant" {
  type        = bool
  description = "Use ZoneRedundant PostgreSQL HA; false uses SameZone. Verify regional support."
}

variable "region_key" {
  type        = string
  description = "Stable regional key."
}

variable "registry_id" {
  type        = string
  description = "Premium ACR ARM ID."
}

variable "services" {
  type        = map(object({ db_key = string, project = string, service_name = string }))
  description = "Nine service ownership records."
}

variable "tags" {
  type        = map(string)
  description = "Shared tags."
}

variable "tenant_id" {
  type        = string
  description = "Resource tenant GUID."
}

variable "vnet_cidr" {
  type        = string
  description = "Regional /16 IPv4 network."
}

variable "zones" {
  type        = list(string)
  description = "Supported AKS availability zones."
}
