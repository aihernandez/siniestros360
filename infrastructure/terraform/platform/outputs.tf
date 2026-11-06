output "regions" {
  description = "Regional resource metadata used by private data/workload/edge stages. No Kubernetes credentials."
  value       = { for key, region in module.region : key => region.metadata }
}
output "registry" {
  description = "Private ACR image distribution metadata. Push from a runner with regional private DNS/connectivity."
  value       = { id = azurerm_container_registry.image.id, login_server = azurerm_container_registry.image.login_server }
}
output "bootstrap_credentials" {
  description = "Sensitive bootstrap material for regional-data only. Backend readers can access these values; restrict state RBAC."
  value       = { database_admin = { for k, value in random_password.database_admin : k => value.result }, legacy_jwt = random_password.legacy_jwt.result, telemetry = { for k, region in module.region : k => region.telemetry_connection_string } }
  sensitive   = true
}
output "tenant_id" {
  description = "Workforce/resource tenant ID."
  value       = var.tenant_id
}
