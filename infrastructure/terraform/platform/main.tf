locals {
  services = jsondecode(file("${path.module}/../services.json"))
  name     = "${var.name_prefix}-${var.environment}"
  tags     = { Project = "Siniestros360", Environment = var.environment, ManagedBy = "Terraform" }
}
resource "azurerm_resource_group" "global" {
  name     = "rg-${local.name}-global"
  location = values(var.regions)[0].location
  tags     = local.tags
}
resource "azurerm_container_registry" "image" {
  name                          = "${var.name_prefix}${var.environment}${substr(sha256(var.subscription_id), 0, 6)}acr"
  resource_group_name           = azurerm_resource_group.global.name
  location                      = azurerm_resource_group.global.location
  sku                           = "Premium"
  admin_enabled                 = false
  public_network_access_enabled = false
  dynamic "georeplications" {
    for_each = { for k, r in var.regions : k => r if r.location != azurerm_resource_group.global.location }
    content {
      location                  = georeplications.value.location
      zone_redundancy_enabled   = true
      regional_endpoint_enabled = true
      tags                      = local.tags
    }
  }
  tags = local.tags
}
resource "random_password" "legacy_jwt" {
  length  = 64
  special = false
}
resource "random_password" "database_admin" {
  for_each = var.regions
  length   = 40
  special  = false
}
module "region" {
  for_each                = var.regions
  source                  = "../modules/regional"
  name                    = "${local.name}-${each.key}"
  region_key              = each.key
  location                = each.value.location
  vnet_cidr               = each.value.vnet_cidr
  zones                   = each.value.zones
  postgres_zone_redundant = each.value.postgres_zone_redundant
  admin_password          = random_password.database_admin[each.key].result
  admin_group_object_ids  = var.admin_group_object_ids
  operator_object_id      = var.operator_object_id
  tenant_id               = var.tenant_id
  kubernetes_version      = var.kubernetes_version
  registry_id             = azurerm_container_registry.image.id
  services                = local.services
  tags                    = local.tags
  alert_email             = var.alert_email
}
