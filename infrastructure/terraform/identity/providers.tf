provider "azuread" {
  alias     = "workforce"
  tenant_id = var.workforce_tenant_id
}
provider "azuread" {
  alias     = "customers"
  tenant_id = var.customer_tenant_id
}
