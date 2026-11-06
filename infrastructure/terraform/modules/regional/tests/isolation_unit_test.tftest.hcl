mock_provider "azurerm" {
  mock_resource "azurerm_kubernetes_cluster" {
    defaults = {
      kubelet_identity = [{ client_id = "00000000-0000-0000-0000-000000000005", object_id = "00000000-0000-0000-0000-000000000006", user_assigned_identity_id = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/test/providers/Microsoft.ManagedIdentity/userAssignedIdentities/kubelet" }]
    }
  }
}

variables {
  name                    = "s360-test-centralus"
  region_key              = "centralus"
  location                = "centralus"
  vnet_cidr               = "10.40.0.0/16"
  zones                   = ["1", "2", "3"]
  postgres_zone_redundant = true
  kubernetes_version      = null
  admin_password          = "not-a-real-password-used-only-in-mocked-plans"
  tenant_id               = "00000000-0000-0000-0000-000000000001"
  operator_object_id      = "00000000-0000-0000-0000-000000000002"
  admin_group_object_ids  = ["00000000-0000-0000-0000-000000000003"]
  registry_id             = "/subscriptions/00000000-0000-0000-0000-000000000004/resourceGroups/test/providers/Microsoft.ContainerRegistry/registries/test"
  alert_email             = "operations@example.com"
  tags                    = {}
  services = {
    identity   = { db_key = "identitydb", project = "Identity", service_name = "identity-service" }
    claims     = { db_key = "claimsdb", project = "Claims", service_name = "claims-service" }
    adjusters  = { db_key = "adjustersdb", project = "Adjusters", service_name = "adjusters-service" }
    location   = { db_key = "locationsdb", project = "Location", service_name = "location-service" }
    dispatch   = { db_key = "dispatchdb", project = "Dispatch", service_name = "dispatch-service" }
    policy     = { db_key = "policydb", project = "Policy", service_name = "policy-service" }
    sla        = { db_key = "sladb", project = "Sla", service_name = "sla-service" }
    documents  = { db_key = "documentsdb", project = "Documents", service_name = "documents-service" }
    operations = { db_key = "operationsdb", project = "Operations", service_name = "operations-service" }
  }
}

run "private_dependencies_and_workload_federation" {
  command = plan
  assert {
    condition     = azurerm_kubernetes_cluster.application.private_cluster_enabled && azurerm_kubernetes_cluster.application.local_account_disabled && azurerm_kubernetes_cluster.application.workload_identity_enabled
    error_message = "AKS must be private, use Entra access and enable workload identity."
  }
  assert {
    condition     = !azurerm_postgresql_flexible_server.database.public_network_access_enabled && !azurerm_storage_account.documents.public_network_access_enabled && !azurerm_key_vault.configuration.public_network_access_enabled && !azurerm_servicebus_namespace.messaging.public_network_access_enabled && !azurerm_servicebus_namespace.messaging.local_auth_enabled
    error_message = "Data dependencies must be private and Service Bus must reject local SAS authentication."
  }
  assert {
    condition     = length(azurerm_private_endpoint.dependency) == 4 && length(azurerm_postgresql_flexible_server_database.service) == 9 && length(azurerm_federated_identity_credential.service) == 11 && length(azurerm_role_assignment.messaging) == 8
    error_message = "Service ownership, private endpoints or identity privilege inventory changed."
  }
  assert {
    condition     = output.metadata.ingress_backend_ip == "10.40.20.20" && azurerm_kubernetes_cluster.application.network_profile[0].outbound_type == "userAssignedNATGateway"
    error_message = "The fixed ILB address must be outside the AKS node subnet and outbound traffic must use the configured NAT."
  }
}
