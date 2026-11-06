resource "azurerm_resource_group" "edge" {
  name     = "rg-${var.name}-edge"
  location = values(local.regions)[0].location
}
resource "azurerm_dns_zone" "application" {
  name                = var.base_domain
  resource_group_name = azurerm_resource_group.edge.name
}
module "region" {
  for_each              = var.geographies
  source                = "../modules/edge"
  region                = local.regions[each.key]
  certificate_secret_id = data.terraform_remote_state.regional_data[each.key].outputs.tls_secret_id
  hostnames             = local.hostnames
  enable_public_traffic = var.enable_public_traffic
  tags                  = { Project = "Siniestros360", ManagedBy = "Terraform" }
  depends_on            = [terraform_data.readiness]
}
resource "azurerm_traffic_manager_profile" "geographic" {
  name                   = "tm-${var.name}-geographic"
  resource_group_name    = azurerm_resource_group.edge.name
  traffic_routing_method = "Geographic"
  dns_config {
    relative_name = "${var.name}-geographic"
    ttl           = 60
  }
  monitor_config {
    protocol = "HTTPS"
    port     = 443
    path     = "/health"
    custom_header {
      name  = "Host"
      value = "api.${var.base_domain}"
    }
  }
}
resource "azurerm_traffic_manager_profile" "regional" {
  for_each               = var.geographies
  name                   = "tm-${var.name}-${each.key}"
  resource_group_name    = azurerm_resource_group.edge.name
  traffic_routing_method = "Priority"
  dns_config {
    relative_name = "${var.name}-${each.key}"
    ttl           = 60
  }
  monitor_config {
    protocol = "HTTPS"
    port     = 443
    path     = "/health"
    custom_header {
      name  = "Host"
      value = "api.${var.base_domain}"
    }
  }
}
resource "azurerm_traffic_manager_external_endpoint" "primary" {
  for_each   = var.geographies
  name       = "primary"
  profile_id = azurerm_traffic_manager_profile.regional[each.key].id
  target     = module.region[each.key].fqdn
  priority   = 1
  weight     = 1
  enabled    = var.enable_public_traffic
}
resource "azurerm_traffic_manager_external_endpoint" "recovery" {
  for_each   = var.recovery_targets
  name       = each.key
  profile_id = azurerm_traffic_manager_profile.regional[each.value.region].id
  target     = each.value.fqdn
  priority   = each.value.priority
  weight     = 1
  enabled    = var.enable_public_traffic
}
resource "azurerm_traffic_manager_nested_endpoint" "geographic" {
  for_each                = var.geographies
  name                    = each.key
  profile_id              = azurerm_traffic_manager_profile.geographic.id
  target_resource_id      = azurerm_traffic_manager_profile.regional[each.key].id
  minimum_child_endpoints = 1
  geo_mappings            = concat(tolist(each.value), var.world_fallback_region == each.key ? ["WORLD"] : [])
  weight                  = 1
  enabled                 = var.enable_public_traffic
}
resource "azurerm_dns_cname_record" "application" {
  for_each            = toset(["api", "insured", "adjuster", "tower"])
  name                = each.key
  resource_group_name = azurerm_resource_group.edge.name
  zone_name           = azurerm_dns_zone.application.name
  ttl                 = 60
  record              = azurerm_traffic_manager_profile.geographic.fqdn
}
output "dns_delegation" {
  description = "Delegate this child zone at the parent registrar/DNS provider; Terraform does not assume ownership of your parent zone."
  value       = azurerm_dns_zone.application.name_servers
}
output "application_urls" {
  description = "Global hostnames; disabled DNS health endpoints until enable_public_traffic and readiness confirmations."
  value       = { for app in ["api", "insured", "adjuster", "tower"] : app => "https://${app}.${var.base_domain}" }
}
