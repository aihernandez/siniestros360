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
