resource "azurerm_postgresql_flexible_server" "database" {
  name                          = "pg-${var.name}"
  resource_group_name           = azurerm_resource_group.regional.name
  location                      = var.location
  version                       = "16"
  sku_name                      = "GP_Standard_D4ds_v5"
  storage_mb                    = 131072
  administrator_login           = "s360admin"
  administrator_password        = var.admin_password
  delegated_subnet_id           = azurerm_subnet.regional["postgres"].id
  private_dns_zone_id           = azurerm_private_dns_zone.regional["postgres"].id
  public_network_access_enabled = false
  backup_retention_days         = 35
  geo_redundant_backup_enabled  = false
  high_availability { mode = var.postgres_zone_redundant ? "ZoneRedundant" : "SameZone" }
  authentication { password_auth_enabled = true }
  depends_on = [azurerm_private_dns_zone_virtual_network_link.regional]
  lifecycle { prevent_destroy = true }
  tags = var.tags
}
resource "azurerm_postgresql_flexible_server_configuration" "extension" {
  name      = "azure.extensions"
  server_id = azurerm_postgresql_flexible_server.database.id
  value     = "POSTGIS"
}
resource "azurerm_postgresql_flexible_server_database" "service" {
  for_each  = var.services
  name      = "siniestros360_${each.key}"
  server_id = azurerm_postgresql_flexible_server.database.id
  collation = "en_US.utf8"
  charset   = "UTF8"
  lifecycle { prevent_destroy = true }
}
resource "azurerm_servicebus_namespace" "messaging" {
  name                          = "sb-${var.name}"
  resource_group_name           = azurerm_resource_group.regional.name
  location                      = var.location
  sku                           = "Premium"
  capacity                      = 1
  premium_messaging_partitions  = 1
  public_network_access_enabled = false
  local_auth_enabled            = false
  minimum_tls_version           = "1.2"
  lifecycle { prevent_destroy = true }
  tags = var.tags
}
resource "azurerm_storage_account" "documents" {
  name                            = "${local.compact_name}${substr(sha256(var.name), 0, 4)}doc"
  resource_group_name             = azurerm_resource_group.regional.name
  location                        = var.location
  account_tier                    = "Standard"
  account_replication_type        = "ZRS"
  min_tls_version                 = "TLS1_2"
  public_network_access_enabled   = false
  allow_nested_items_to_be_public = false
  # Existing REST adapter signs requests with a container SAS; replace with SDK workload identity before disabling shared keys.
  shared_access_key_enabled = true
  blob_properties {
    versioning_enabled = true
    delete_retention_policy { days = 30 }
    container_delete_retention_policy { days = 30 }
  }
  lifecycle { prevent_destroy = true }
  tags = var.tags
}
resource "azurerm_key_vault" "configuration" {
  name                          = "kv-${substr(var.name, 0, 17)}-${substr(sha256(var.name), 0, 3)}"
  resource_group_name           = azurerm_resource_group.regional.name
  location                      = var.location
  tenant_id                     = var.tenant_id
  sku_name                      = "standard"
  rbac_authorization_enabled    = true
  purge_protection_enabled      = true
  soft_delete_retention_days    = 90
  public_network_access_enabled = false
  lifecycle { prevent_destroy = true }
  tags = var.tags
}
resource "azurerm_role_assignment" "secret_operator" {
  scope                = azurerm_key_vault.configuration.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = var.operator_object_id
}
resource "azurerm_role_assignment" "certificate_operator" {
  scope                = azurerm_key_vault.configuration.id
  role_definition_name = "Key Vault Certificates Officer"
  principal_id         = var.operator_object_id
}
resource "azurerm_private_endpoint" "dependency" {
  for_each = {
    vault      = { id = azurerm_key_vault.configuration.id, subresource = "vault" }
    blob       = { id = azurerm_storage_account.documents.id, subresource = "blob" }
    servicebus = { id = azurerm_servicebus_namespace.messaging.id, subresource = "namespace" }
    registry   = { id = var.registry_id, subresource = "registry" }
  }
  name                = "pe-${var.name}-${each.key}"
  resource_group_name = azurerm_resource_group.regional.name
  location            = var.location
  subnet_id           = azurerm_subnet.regional["endpoints"].id
  private_service_connection {
    name                           = each.key
    private_connection_resource_id = each.value.id
    subresource_names              = [each.value.subresource]
    is_manual_connection           = false
  }
  private_dns_zone_group {
    name                 = "regional"
    private_dns_zone_ids = [azurerm_private_dns_zone.regional[each.key].id]
  }
  tags = var.tags
}
