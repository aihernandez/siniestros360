data "terraform_remote_state" "platform" {
  backend = "azurerm"
  config = {
    resource_group_name  = var.state.resource_group_name
    storage_account_name = var.state.storage_account_name
    container_name       = var.state.container_name
    key                  = var.state.platform_key
    use_azuread_auth     = true
  }
}
locals {
  region   = data.terraform_remote_state.platform.outputs.regions[var.region_key]
  services = jsondecode(file("${path.module}/../services.json"))
}

data "terraform_remote_state" "configuration" {
  backend = "azurerm"
  config  = { resource_group_name = var.state.resource_group_name, storage_account_name = var.state.storage_account_name, container_name = var.state.container_name, key = var.data_state_key, use_azuread_auth = true }
}
locals {
  backends   = merge(local.services, { gateway = { db_key = "", project = "Gateway", service_name = "gateway" } })
  frontends  = toset(["insured-app", "adjuster-app", "control-tower"])
  pod_labels = { "azure.workload.identity/use" = "true" }
}
resource "terraform_data" "images" {
  input = var.release_id
  lifecycle {
    precondition {
      condition     = alltrue([for key in keys(local.backends) : contains(keys(var.service_images), key)]) && alltrue([for key in local.frontends : contains(keys(var.frontend_images), key)])
      error_message = "Provide all ten backend and three frontend images."
    }
    precondition {
      condition     = alltrue([for image in concat(values(var.service_images), values(var.frontend_images), values(var.migration_images), [var.proxy_image, var.collector_image]) : (can(regex("@sha256:[a-f0-9]{64}$", image)) || (!strcontains(image, "@") && can(regex(":[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$", image)))) && !endswith(lower(image), ":latest")])
      error_message = "Use explicit image tags or digests; latest/unversioned images are rejected."
    }
  }
}
