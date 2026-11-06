resource "azurerm_resource_group" "identity" {
  name     = "rg-${var.name}-identity"
  location = var.resource_group_location
}
resource "azapi_resource" "customer_tenant" {
  type      = "Microsoft.AzureActiveDirectory/ciamDirectories@2023-05-17-preview"
  name      = var.name
  parent_id = azurerm_resource_group.identity.id
  location  = var.data_residency_location
  body = {
    properties = { createTenantProperties = { countryCode = var.country_code, displayName = "Siniestros360 customers" } }
    sku        = { name = "Standard", tier = "A0" }
  }
  response_export_values = ["properties.tenantId"]
  lifecycle { prevent_destroy = true }
}
output "customer_tenant_id" {
  description = "Authenticate/consent in this new tenant before running the separate identity registration stack."
  value       = azapi_resource.customer_tenant.output.properties.tenantId
}
