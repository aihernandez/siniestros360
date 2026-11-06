variable "allowed_ip_cidrs" {
  type        = set(string)
  description = "Public operator/runner IPv4 CIDRs permitted to reach the state account; include regional NAT IPs before running private stages."
  validation {
    condition     = length(var.allowed_ip_cidrs) > 0 && alltrue([for value in var.allowed_ip_cidrs : can(cidrnetmask(value)) && !endswith(value, "/0")])
    error_message = "At least one explicit IPv4 operator CIDR is required; /0 is prohibited."
  }
}

variable "location" {
  type        = string
  description = "Region for the state backend."
  default     = "centralus"
}

variable "allowed_subnet_ids" {
  type        = set(string)
  description = "Regional runner subnet ARM IDs with Microsoft.Storage.Global service endpoints; add after platform creation."
  default     = []
}

variable "name" {
  type        = string
  description = "Globally unique state account name, 3-24 lowercase letters/digits."
  validation {
    condition     = can(regex("^[a-z0-9]{3,24}$", var.name))
    error_message = "State account names require 3-24 lowercase letters/digits."
  }
}

variable "operator_object_id" {
  type        = string
  description = "Entra object ID granted Blob Data Contributor on the state account."
}

variable "subscription_id" {
  type        = string
  description = "Azure subscription ID; authenticate through Azure CLI or workload federation."
}
