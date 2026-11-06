resource "azurerm_resource_group" "state" {
  name     = "rg-${var.name}-tfstate"
  location = var.location
}
resource "azurerm_storage_account" "state" {
  name                            = var.name
  resource_group_name             = azurerm_resource_group.state.name
  location                        = var.location
  account_tier                    = "Standard"
  account_replication_type        = "ZRS"
  min_tls_version                 = "TLS1_2"
  shared_access_key_enabled       = false
  default_to_oauth_authentication = true
  allow_nested_items_to_be_public = false
  network_rules {
    default_action             = "Deny"
    bypass                     = ["AzureServices"]
    ip_rules                   = [for cidr in var.allowed_ip_cidrs : replace(cidr, "/32", "")]
    virtual_network_subnet_ids = var.allowed_subnet_ids
  }
  blob_properties {
    versioning_enabled = true
    delete_retention_policy { days = 30 }
    container_delete_retention_policy { days = 30 }
  }
  lifecycle { prevent_destroy = true }
  tags = { ManagedBy = "Terraform", Purpose = "Protected state" }
}
resource "azurerm_role_assignment" "operator" {
  scope                = azurerm_storage_account.state.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = var.operator_object_id
}
resource "azurerm_storage_container" "state" {
  name                  = "tfstate"
  storage_account_id    = azurerm_storage_account.state.id
  container_access_type = "private"
  depends_on            = [azurerm_role_assignment.operator]
  lifecycle { prevent_destroy = true }
}
output "backend" {
  description = "Backend configuration; set a different key per root/region. Preserve and migrate bootstrap local state separately."
  value       = { resource_group_name = azurerm_resource_group.state.name, storage_account_name = azurerm_storage_account.state.name, container_name = azurerm_storage_container.state.name, use_azuread_auth = true }
}
