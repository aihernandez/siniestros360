output "metadata" {
  description = "Non-password metadata for subsequent deployment stages. Application Insights connection is instrumentation metadata."
  value = {
    name                  = var.name
    resource_group_name   = azurerm_resource_group.regional.name
    location              = var.location
    vnet_id               = azurerm_virtual_network.regional.id
    subnets               = { for key, subnet in azurerm_subnet.regional : key => { id = subnet.id, cidr = subnet.address_prefixes[0] } }
    ingress_backend_ip    = cidrhost(azurerm_subnet.regional["loadbalancer"].address_prefixes[0], 20)
    egress_ip             = azurerm_public_ip.egress.ip_address
    cluster_dns_ip        = azurerm_kubernetes_cluster.application.network_profile[0].dns_service_ip
    cluster_name          = azurerm_kubernetes_cluster.application.name
    postgres_fqdn         = azurerm_postgresql_flexible_server.database.fqdn
    database_names        = { for key, database in azurerm_postgresql_flexible_server_database.service : key => database.name }
    key_vault_id          = azurerm_key_vault.configuration.id
    key_vault_name        = azurerm_key_vault.configuration.name
    servicebus_namespace  = azurerm_servicebus_namespace.messaging.name
    storage_account_name  = azurerm_storage_account.documents.name
    storage_blob_endpoint = azurerm_storage_account.documents.primary_blob_endpoint
    edge_identity_id      = azurerm_user_assigned_identity.edge.id
    log_workspace_id      = azurerm_log_analytics_workspace.regional.id
    action_group_id       = azurerm_monitor_action_group.operations.id
    service_identities    = { for key, identity in azurerm_user_assigned_identity.service : key => { client_id = identity.client_id, principal_id = identity.principal_id } }
  }
}

output "telemetry_connection_string" {
  description = "Application Insights ingestion connection for the secret provisioning stage."
  value       = azurerm_application_insights.application.connection_string
  sensitive   = true
}
