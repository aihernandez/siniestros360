resource "azurerm_log_analytics_workspace" "regional" {
  name                = "log-${var.name}"
  resource_group_name = azurerm_resource_group.regional.name
  location            = var.location
  sku                 = "PerGB2018"
  retention_in_days   = 30
  tags                = var.tags
}
resource "azurerm_application_insights" "application" {
  name                = "appi-${var.name}"
  resource_group_name = azurerm_resource_group.regional.name
  location            = var.location
  workspace_id        = azurerm_log_analytics_workspace.regional.id
  application_type    = "web"
  tags                = var.tags
}
resource "azurerm_monitor_action_group" "operations" {
  name                = "ag-${var.name}"
  resource_group_name = azurerm_resource_group.regional.name
  short_name          = "s360ops"
  email_receiver {
    name          = "operations"
    email_address = var.alert_email
  }
  tags = var.tags
}
resource "azurerm_monitor_metric_alert" "database_cpu" {
  name                = "alert-${var.name}-database-cpu"
  resource_group_name = azurerm_resource_group.regional.name
  scopes              = [azurerm_postgresql_flexible_server.database.id]
  severity            = 2
  frequency           = "PT1M"
  window_size         = "PT5M"
  criteria {
    metric_namespace = "Microsoft.DBforPostgreSQL/flexibleServers"
    metric_name      = "cpu_percent"
    aggregation      = "Average"
    operator         = "GreaterThan"
    threshold        = 80
  }
  action { action_group_id = azurerm_monitor_action_group.operations.id }
  tags = var.tags
}
resource "azurerm_monitor_metric_alert" "dead_letters" {
  name                = "alert-${var.name}-deadletters"
  resource_group_name = azurerm_resource_group.regional.name
  scopes              = [azurerm_servicebus_namespace.messaging.id]
  severity            = 2
  frequency           = "PT1M"
  window_size         = "PT5M"
  criteria {
    metric_namespace = "Microsoft.ServiceBus/namespaces"
    metric_name      = "DeadletteredMessages"
    aggregation      = "Maximum"
    operator         = "GreaterThan"
    threshold        = 0
  }
  action { action_group_id = azurerm_monitor_action_group.operations.id }
  tags = var.tags
}
resource "azurerm_monitor_diagnostic_setting" "resource" {
  for_each = {
    aks        = azurerm_kubernetes_cluster.application.id
    postgres   = azurerm_postgresql_flexible_server.database.id
    vault      = azurerm_key_vault.configuration.id
    servicebus = azurerm_servicebus_namespace.messaging.id
  }
  name                       = "regional-logs"
  target_resource_id         = each.value
  log_analytics_workspace_id = azurerm_log_analytics_workspace.regional.id
  enabled_log { category_group = "allLogs" }
  enabled_metric { category = "AllMetrics" }
}
