resource "azurerm_user_assigned_identity" "cluster" {
  name                = "id-${var.name}-aks"
  resource_group_name = azurerm_resource_group.regional.name
  location            = var.location
  tags                = var.tags
}
resource "azurerm_role_assignment" "network" {
  scope                = azurerm_virtual_network.regional.id
  role_definition_name = "Network Contributor"
  principal_id         = azurerm_user_assigned_identity.cluster.principal_id
}
resource "azurerm_kubernetes_cluster" "application" {
  name                                = "aks-${var.name}"
  resource_group_name                 = azurerm_resource_group.regional.name
  location                            = var.location
  dns_prefix                          = "aks-${var.name}"
  kubernetes_version                  = var.kubernetes_version
  private_cluster_enabled             = true
  private_cluster_public_fqdn_enabled = false
  local_account_disabled              = true
  role_based_access_control_enabled   = true
  oidc_issuer_enabled                 = true
  workload_identity_enabled           = true
  azure_policy_enabled                = true
  sku_tier                            = "Standard"
  automatic_upgrade_channel           = "patch"
  node_os_upgrade_channel             = "NodeImage"
  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.cluster.id]
  }
  kubelet_identity {}
  azure_active_directory_role_based_access_control {
    azure_rbac_enabled     = true
    tenant_id              = var.tenant_id
    admin_group_object_ids = var.admin_group_object_ids
  }
  default_node_pool {
    name                         = "system"
    vm_size                      = "Standard_D4ds_v5"
    vnet_subnet_id               = azurerm_subnet.regional["aks"].id
    zones                        = var.zones
    node_count                   = 3
    auto_scaling_enabled         = true
    min_count                    = 3
    max_count                    = 6
    only_critical_addons_enabled = true
    upgrade_settings { max_surge = "33%" }
  }
  network_profile {
    network_plugin      = "azure"
    network_plugin_mode = "overlay"
    network_policy      = "cilium"
    network_data_plane  = "cilium"
    pod_cidr            = "172.20.0.0/16"
    service_cidr        = "172.21.0.0/16"
    dns_service_ip      = "172.21.0.10"
    outbound_type       = "userAssignedNATGateway"
    load_balancer_sku   = "standard"
  }
  key_vault_secrets_provider { secret_rotation_enabled = true }
  oms_agent {
    log_analytics_workspace_id      = azurerm_log_analytics_workspace.regional.id
    msi_auth_for_monitoring_enabled = true
  }
  depends_on = [azurerm_role_assignment.network, azurerm_subnet_nat_gateway_association.egress, azurerm_nat_gateway_public_ip_association.egress]
  tags       = var.tags
}
resource "azurerm_kubernetes_cluster_node_pool" "application" {
  name                  = "apps"
  kubernetes_cluster_id = azurerm_kubernetes_cluster.application.id
  vm_size               = "Standard_D4ds_v5"
  vnet_subnet_id        = azurerm_subnet.regional["aks"].id
  zones                 = var.zones
  mode                  = "User"
  auto_scaling_enabled  = true
  min_count             = 3
  max_count             = 12
  node_count            = 3
  upgrade_settings { max_surge = "33%" }
  tags = var.tags
}
resource "azurerm_role_assignment" "pull" {
  scope                            = var.registry_id
  role_definition_name             = "AcrPull"
  principal_id                     = azurerm_kubernetes_cluster.application.kubelet_identity[0].object_id
  skip_service_principal_aad_check = true
}
resource "azurerm_role_assignment" "cluster_operator" {
  scope                = azurerm_kubernetes_cluster.application.id
  role_definition_name = "Azure Kubernetes Service RBAC Cluster Admin"
  principal_id         = var.operator_object_id
}
resource "azurerm_role_assignment" "cluster_credentials" {
  scope                = azurerm_kubernetes_cluster.application.id
  role_definition_name = "Azure Kubernetes Service Cluster User Role"
  principal_id         = var.operator_object_id
}
resource "azurerm_user_assigned_identity" "service" {
  for_each            = merge(var.services, { gateway = { db_key = "", project = "Gateway", service_name = "gateway" }, telemetry = { db_key = "", project = "Collector", service_name = "telemetry" } })
  name                = "id-${var.name}-${each.key}"
  resource_group_name = azurerm_resource_group.regional.name
  location            = var.location
  tags                = var.tags
}
resource "azurerm_federated_identity_credential" "service" {
  for_each  = azurerm_user_assigned_identity.service
  name      = "kubernetes"
  parent_id = each.value.id
  issuer    = azurerm_kubernetes_cluster.application.oidc_issuer_url
  audience  = ["api://AzureADTokenExchange"]
  subject   = "system:serviceaccount:siniestros360:${each.key}"
}
resource "azurerm_role_assignment" "messaging" {
  for_each = { for k, v in azurerm_user_assigned_identity.service : k => v if !contains(["identity", "gateway", "telemetry"], k) }
  scope    = azurerm_servicebus_namespace.messaging.id
  # Current MassTransit provisions its own topology, so Send/Listen alone is insufficient.
  role_definition_name = "Azure Service Bus Data Owner"
  principal_id         = each.value.principal_id
}
resource "azurerm_user_assigned_identity" "edge" {
  name                = "id-${var.name}-edge"
  resource_group_name = azurerm_resource_group.regional.name
  location            = var.location
  tags                = var.tags
}
