provider "azurerm" {
  subscription_id                 = var.subscription_id
  resource_provider_registrations = "none"
  storage_use_azuread             = true
  features {}
}

provider "postgresql" {
  host             = local.region.postgres_fqdn
  port             = 5432
  database         = "postgres"
  username         = "s360admin"
  password         = data.terraform_remote_state.platform.outputs.bootstrap_credentials.database_admin[var.region_key]
  sslmode          = "verify-full"
  sslrootcert      = var.postgresql_ssl_root_certificate
  superuser        = false
  expected_version = "16.0.0"
}
