locals {
  compact_name = substr(replace(var.name, "-", ""), 0, 17)
  subnets = {
    aks          = { cidr = cidrsubnet(var.vnet_cidr, 4, 0), delegation = false }
    ingress      = { cidr = cidrsubnet(var.vnet_cidr, 8, 16), delegation = false }
    postgres     = { cidr = cidrsubnet(var.vnet_cidr, 8, 17), delegation = true }
    endpoints    = { cidr = cidrsubnet(var.vnet_cidr, 8, 18), delegation = false }
    runner       = { cidr = cidrsubnet(var.vnet_cidr, 8, 19), delegation = false }
    loadbalancer = { cidr = cidrsubnet(var.vnet_cidr, 8, 20), delegation = false }
  }
  private_zones = {
    postgres   = "private.postgres.database.azure.com"
    vault      = "privatelink.vaultcore.azure.net"
    blob       = "privatelink.blob.core.windows.net"
    servicebus = "privatelink.servicebus.windows.net"
    registry   = "privatelink.azurecr.io"
  }
}
resource "azurerm_resource_group" "regional" {
  name     = "rg-${var.name}"
  location = var.location
  tags     = var.tags
}
resource "azurerm_virtual_network" "regional" {
  name                = "vnet-${var.name}"
  resource_group_name = azurerm_resource_group.regional.name
  location            = var.location
  address_space       = [var.vnet_cidr]
  tags                = var.tags
}
resource "azurerm_subnet" "regional" {
  for_each                          = local.subnets
  name                              = each.key
  resource_group_name               = azurerm_resource_group.regional.name
  virtual_network_name              = azurerm_virtual_network.regional.name
  address_prefixes                  = [each.value.cidr]
  service_endpoints                 = each.key == "runner" ? ["Microsoft.Storage.Global"] : []
  private_endpoint_network_policies = "Disabled"
  dynamic "delegation" {
    for_each = each.value.delegation ? [1] : []
    content {
      name = "postgres"
      service_delegation {
        name    = "Microsoft.DBforPostgreSQL/flexibleServers"
        actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
      }
    }
  }
}
resource "azurerm_network_security_group" "ingress" {
  name                = "nsg-${var.name}-ingress"
  location            = var.location
  resource_group_name = azurerm_resource_group.regional.name
  security_rule {
    name                       = "GatewayManager"
    priority                   = 100
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "65200-65535"
    source_address_prefix      = "GatewayManager"
    destination_address_prefix = "*"
  }
  security_rule {
    name                       = "HTTPS"
    priority                   = 110
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "443"
    source_address_prefix      = "Internet"
    destination_address_prefix = "*"
  }
  tags = var.tags
}
resource "azurerm_subnet_network_security_group_association" "ingress" {
  subnet_id                 = azurerm_subnet.regional["ingress"].id
  network_security_group_id = azurerm_network_security_group.ingress.id
}
resource "azurerm_public_ip" "egress" {
  name                = "pip-${var.name}-egress"
  resource_group_name = azurerm_resource_group.regional.name
  location            = var.location
  allocation_method   = "Static"
  sku                 = "Standard"
  tags                = var.tags
}
resource "azurerm_nat_gateway" "egress" {
  name                    = "nat-${var.name}"
  resource_group_name     = azurerm_resource_group.regional.name
  location                = var.location
  sku_name                = "Standard"
  idle_timeout_in_minutes = 10
  tags                    = var.tags
}
resource "azurerm_nat_gateway_public_ip_association" "egress" {
  nat_gateway_id       = azurerm_nat_gateway.egress.id
  public_ip_address_id = azurerm_public_ip.egress.id
}
resource "azurerm_subnet_nat_gateway_association" "egress" {
  for_each       = toset(["aks", "runner"])
  subnet_id      = azurerm_subnet.regional[each.key].id
  nat_gateway_id = azurerm_nat_gateway.egress.id
}
resource "azurerm_private_dns_zone" "regional" {
  for_each            = local.private_zones
  name                = each.value
  resource_group_name = azurerm_resource_group.regional.name
  tags                = var.tags
}
resource "azurerm_private_dns_zone_virtual_network_link" "regional" {
  for_each              = local.private_zones
  name                  = "regional"
  resource_group_name   = azurerm_resource_group.regional.name
  private_dns_zone_name = azurerm_private_dns_zone.regional[each.key].name
  virtual_network_id    = azurerm_virtual_network.regional.id
  registration_enabled  = false
}
