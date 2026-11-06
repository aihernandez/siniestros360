resource "azurerm_public_ip" "entry" {
  name                = "pip-${var.region.name}-entry"
  resource_group_name = var.region.resource_group_name
  location            = var.region.location
  allocation_method   = "Static"
  sku                 = "Standard"
  domain_name_label   = "${var.region.name}-entry"
  tags                = var.tags
}
resource "azurerm_web_application_firewall_policy" "application" {
  name                = "waf-${var.region.name}"
  resource_group_name = var.region.resource_group_name
  location            = var.region.location
  policy_settings {
    enabled                     = true
    mode                        = "Prevention"
    request_body_check          = true
    max_request_body_size_in_kb = 128
    file_upload_limit_in_mb     = 20
  }
  dynamic "custom_rules" {
    for_each = var.enable_public_traffic ? [] : [true]
    content {
      name      = "PublicationDisabled"
      priority  = 1
      rule_type = "MatchRule"
      action    = "Block"
      match_conditions {
        match_variables { variable_name = "RemoteAddr" }
        operator     = "IPMatch"
        match_values = ["0.0.0.0/0"]
      }
    }
  }
  custom_rules {
    name      = "RejectUnknownHost"
    priority  = 10
    rule_type = "MatchRule"
    action    = "Block"
    match_conditions {
      match_variables {
        variable_name = "RequestHeaders"
        selector      = "Host"
      }
      operator           = "Equal"
      negation_condition = true
      match_values       = var.hostnames
      transforms         = ["Lowercase"]
    }
  }
  managed_rules {
    managed_rule_set {
      type    = "OWASP"
      version = "3.2"
    }
  }
  tags = var.tags
}
resource "azurerm_application_gateway" "regional" {
  name                = "agw-${var.region.name}"
  resource_group_name = var.region.resource_group_name
  location            = var.region.location
  firewall_policy_id  = azurerm_web_application_firewall_policy.application.id
  http2_enabled       = true
  sku {
    name = "WAF_v2"
    tier = "WAF_v2"
  }
  autoscale_configuration {
    min_capacity = 2
    max_capacity = 10
  }
  identity {
    type         = "UserAssigned"
    identity_ids = [var.region.edge_identity_id]
  }
  gateway_ip_configuration {
    name      = "regional"
    subnet_id = var.region.subnets["ingress"].id
  }
  frontend_port {
    name = "https"
    port = 443
  }
  frontend_ip_configuration {
    name                 = "public"
    public_ip_address_id = azurerm_public_ip.entry.id
  }
  ssl_certificate {
    name                = "regional"
    key_vault_secret_id = var.certificate_secret_id
  }
  ssl_policy {
    policy_type = "Predefined"
    policy_name = "AppGwSslPolicy20220101S"
  }
  http_listener {
    name                           = "https"
    frontend_ip_configuration_name = "public"
    frontend_port_name             = "https"
    protocol                       = "Https"
    ssl_certificate_name           = "regional"
    require_sni                    = false
  }
  backend_address_pool {
    name         = "private-ingress"
    ip_addresses = [var.region.ingress_backend_ip]
  }
  backend_http_settings {
    name                  = "private-http"
    port                  = 8080
    protocol              = "Http"
    cookie_based_affinity = "Disabled"
    request_timeout       = 60
    probe_name            = "readiness"
  }
  probe {
    name                = "readiness"
    protocol            = "Http"
    host                = var.hostnames[0]
    path                = "/health"
    interval            = 30
    timeout             = 10
    unhealthy_threshold = 3
    match { status_code = ["200-399"] }
  }
  request_routing_rule {
    name                       = "application"
    rule_type                  = "Basic"
    priority                   = 100
    http_listener_name         = "https"
    backend_address_pool_name  = "private-ingress"
    backend_http_settings_name = "private-http"
  }
  tags = var.tags
}
resource "azurerm_monitor_diagnostic_setting" "edge" {
  name                       = "regional-logs"
  target_resource_id         = azurerm_application_gateway.regional.id
  log_analytics_workspace_id = var.region.log_workspace_id
  enabled_log { category_group = "allLogs" }
  enabled_metric { category = "AllMetrics" }
}
resource "azurerm_monitor_metric_alert" "unhealthy_backend" {
  name                = "alert-${var.region.name}-unhealthy-entry"
  resource_group_name = var.region.resource_group_name
  scopes              = [azurerm_application_gateway.regional.id]
  severity            = 1
  frequency           = "PT1M"
  window_size         = "PT5M"
  criteria {
    metric_namespace = "Microsoft.Network/applicationGateways"
    metric_name      = "UnhealthyHostCount"
    aggregation      = "Average"
    operator         = "GreaterThan"
    threshold        = 0
  }
  action { action_group_id = var.region.action_group_id }
}
output "fqdn" {
  description = "Public IP DNS name used for regional Traffic Manager health probes, not a replacement for TLS-covered application domains."
  value       = azurerm_public_ip.entry.fqdn
}
