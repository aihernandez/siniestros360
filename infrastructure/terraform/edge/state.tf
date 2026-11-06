data "terraform_remote_state" "platform" {
  backend = "azurerm"
  config  = { resource_group_name = var.state.resource_group_name, storage_account_name = var.state.storage_account_name, container_name = var.state.container_name, key = var.state.platform_key, use_azuread_auth = true }
}
data "terraform_remote_state" "regional_data" {
  for_each = var.geographies
  backend  = "azurerm"
  config   = { resource_group_name = var.state.resource_group_name, storage_account_name = var.state.storage_account_name, container_name = var.state.container_name, key = var.data_keys[each.key], use_azuread_auth = true }
}
locals {
  regions   = data.terraform_remote_state.platform.outputs.regions
  hostnames = [for app in ["api", "insured", "adjuster", "tower"] : "${app}.${var.base_domain}"]
}
resource "terraform_data" "readiness" {
  input = var.enable_public_traffic
  lifecycle {
    precondition {
      condition     = !var.enable_public_traffic || (var.application_readiness_confirmed && var.ownership_routing_confirmed)
      error_message = "Public traffic requires application and regional single-writer readiness confirmations. Terraform cannot implement those business mechanisms."
    }
    precondition {
      condition     = alltrue([for key in keys(var.geographies) : contains(keys(local.regions), key) && contains(keys(var.data_keys), key)])
      error_message = "Every geographic region needs platform and regional-data state."
    }
    precondition {
      condition     = var.world_fallback_region == null ? true : contains(keys(var.geographies), var.world_fallback_region)
      error_message = "The WORLD fallback must be one of the configured geographic region keys."
    }
    precondition {
      condition     = alltrue([for target in values(var.recovery_targets) : contains(keys(var.geographies), target.region) && contains(var.recovery_ready_regions, target.region) && target.priority > 1])
      error_message = "Backup endpoints require a configured region, priority > 1, and explicit recovery readiness."
    }
  }
}
