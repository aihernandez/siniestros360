data "azurerm_storage_account" "documents" {
  name                = local.region.storage_account_name
  resource_group_name = local.region.resource_group_name
}
resource "azurerm_storage_container" "claims" {
  name                  = "claims"
  storage_account_id    = data.azurerm_storage_account.documents.id
  container_access_type = "private"
  lifecycle { prevent_destroy = true }
}
data "azurerm_storage_account_blob_container_sas" "documents" {
  connection_string = data.azurerm_storage_account.documents.primary_connection_string
  container_name    = azurerm_storage_container.claims.name
  https_only        = true
  start             = var.blob_sas_start_utc
  expiry            = var.blob_sas_expiry_utc
  permissions {
    read   = true
    add    = false
    create = true
    write  = true
    delete = true
    list   = false
  }
  lifecycle {
    precondition {
      condition     = try(timecmp(var.blob_sas_expiry_utc, var.blob_sas_start_utc) > 0 && timecmp(var.blob_sas_expiry_utc, timeadd(var.blob_sas_start_utc, "720h")) <= 0, false)
      error_message = "Use valid UTC timestamps with expiry after start and at most 30 days later; rotate before expiry."
    }
  }
}
locals {
  jwt = { Issuer = "Siniestros360.Identity", Audience = "Siniestros360", SigningKey = data.terraform_remote_state.platform.outputs.bootstrap_credentials.legacy_jwt }
  configuration = merge({ for key, service in local.services : key => merge({
    Jwt               = local.jwt
    ConnectionStrings = { (service.db_key) = "Host=${local.region.postgres_fqdn};Port=5432;Database=${local.region.database_names[key]};Username=${postgresql_role.service[key].name};Password=${random_password.service[key].result};SSL Mode=VerifyFull" }
    Database          = { MigrateOnStartup = false }
    Messaging         = { ServiceBus = { FullyQualifiedNamespace = "${local.region.servicebus_namespace}.servicebus.windows.net" } }
    }, key == "documents" ? {
    Storage = { UseAzureBlob = true, BlobServiceUri = local.region.storage_blob_endpoint, SasToken = trimprefix(data.azurerm_storage_account_blob_container_sas.documents.sas, "?") }
    } : {}, key == "policy" ? { PolicyProvider = { Endpoint = var.policy_provider_endpoint } } : {}) }, {
    gateway = { Jwt = local.jwt }
  })
}
resource "azurerm_key_vault_secret" "application" {
  for_each     = merge(local.services, { gateway = { db_key = "", project = "Gateway", service_name = "gateway" } })
  name         = "config-${each.key}"
  value        = jsonencode(local.configuration[each.key])
  key_vault_id = local.region.key_vault_id
  content_type = "application/json"
  lifecycle { prevent_destroy = true }
}
resource "azurerm_key_vault_secret" "telemetry" {
  name = "config-telemetry"
  value = yamlencode({
    receivers  = { otlp = { protocols = { grpc = { endpoint = "0.0.0.0:4317" }, http = { endpoint = "0.0.0.0:4318" } } } }
    processors = { batch = {}, memory_limiter = { check_interval = "1s", limit_mib = 384 } }
    exporters  = { azuremonitor = { connection_string = data.terraform_remote_state.platform.outputs.bootstrap_credentials.telemetry[var.region_key] } }
    service    = { pipelines = { for signal in ["traces", "metrics", "logs"] : signal => { receivers = ["otlp"], processors = ["memory_limiter", "batch"], exporters = ["azuremonitor"] } } }
  })
  key_vault_id = local.region.key_vault_id
  lifecycle { prevent_destroy = true }
}
resource "azurerm_role_assignment" "configuration_read" {
  for_each             = merge(local.services, { gateway = { db_key = "", project = "Gateway", service_name = "gateway" } })
  scope                = "${local.region.key_vault_id}/secrets/${azurerm_key_vault_secret.application[each.key].name}"
  role_definition_name = "Key Vault Secrets User"
  principal_id         = local.region.service_identities[each.key].principal_id
}
resource "azurerm_role_assignment" "telemetry_read" {
  scope                = "${local.region.key_vault_id}/secrets/${azurerm_key_vault_secret.telemetry.name}"
  role_definition_name = "Key Vault Secrets User"
  principal_id         = local.region.service_identities["telemetry"].principal_id
}
resource "azurerm_key_vault_certificate" "edge" {
  name         = "regional-tls"
  key_vault_id = local.region.key_vault_id
  certificate {
    contents = var.tls_pfx_base64
    password = var.tls_pfx_password
  }
  lifecycle { prevent_destroy = true }
}
output "configuration" {
  description = "Only names/versions for deployment rollout; never secret values."
  value       = { for key, value in azurerm_key_vault_secret.application : key => { name = value.name, version = value.version } }
}
output "tls_secret_id" {
  description = "Versionless certificate secret URI consumed by Application Gateway."
  value       = azurerm_key_vault_certificate.edge.versionless_secret_id
}
data "azurerm_user_assigned_identity" "edge" {
  name                = "id-${local.region.name}-edge"
  resource_group_name = local.region.resource_group_name
}
resource "azurerm_role_assignment" "tls_read" {
  scope                = "${local.region.key_vault_id}/secrets/${azurerm_key_vault_certificate.edge.name}"
  role_definition_name = "Key Vault Secrets User"
  principal_id         = data.azurerm_user_assigned_identity.edge.principal_id
}
