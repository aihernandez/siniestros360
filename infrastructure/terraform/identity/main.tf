module "workforce" {
  source               = "../modules/identity"
  providers            = { azuread = azuread.workforce }
  name                 = "${var.name}-workforce"
  owners               = var.workforce_owner_object_ids
  redirect_uris        = var.workforce_redirect_uris
  native_redirect_uris = var.workforce_native_redirect_uris
  roles                = toset(["adjuster", "control_tower", "admin"])
}
module "customers" {
  source               = "../modules/identity"
  providers            = { azuread = azuread.customers }
  name                 = "${var.name}-customers"
  owners               = var.customer_owner_object_ids
  redirect_uris        = var.customer_redirect_uris
  native_redirect_uris = var.customer_native_redirect_uris
  roles                = toset(["insured"])
}
output "application_registration" {
  description = "API/SPA identifiers for the future OIDC client and JWT-validation migration. No client secret is created."
  value       = { workforce = module.workforce.registration, customers = module.customers.registration }
}
