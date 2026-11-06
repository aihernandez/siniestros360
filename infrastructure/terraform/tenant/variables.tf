variable "country_code" {
  type        = string
  description = "Explicit customer tenant country code; confirm corresponding allowed data-residency geography."
  validation {
    condition     = can(regex("^[A-Z]{2}$", var.country_code))
    error_message = "Supply an uppercase two-letter country code."
  }
}

variable "data_residency_location" {
  type        = string
  description = "External ID geography: United States, Europe, Asia Pacific or Australia. This is not an AKS region."
  validation {
    condition     = contains(["United States", "Europe", "Asia Pacific", "Australia"], var.data_residency_location)
    error_message = "Select an External ID geography, not a regional AKS location."
  }
}

variable "name" {
  type        = string
  description = "Unique External ID tenant prefix, 1-26 letters/digits. Creation is optional if a tenant already exists."
  validation {
    condition     = can(regex("^[a-zA-Z0-9]{1,26}$", var.name))
    error_message = "External tenant names allow 1-26 alphanumeric characters."
  }
}

variable "resource_group_location" {
  type        = string
  description = "Location of the tenant's Azure billing resource group."
  default     = "centralus"
}

variable "subscription_id" {
  type        = string
  description = "Azure subscription ID; authenticate through Azure CLI or workload federation."
}
