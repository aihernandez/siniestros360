mock_provider "azurerm" {}
variables {
  certificate_secret_id = "https://example.vault.azure.net/secrets/regional-tls"
  hostnames             = ["api.claims.example.com", "insured.claims.example.com", "adjuster.claims.example.com", "tower.claims.example.com"]
  tags                  = {}
  region = {
    name                = "s360-test-centralus"
    resource_group_name = "rg-test"
    location            = "centralus"
    subnets             = { ingress = { id = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/test/providers/Microsoft.Network/virtualNetworks/test/subnets/ingress", cidr = "10.40.16.0/24" } }
    ingress_backend_ip  = "10.40.20.20"
    edge_identity_id    = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/test/providers/Microsoft.ManagedIdentity/userAssignedIdentities/edge"
    log_workspace_id    = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/test/providers/Microsoft.OperationalInsights/workspaces/test"
    action_group_id     = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/test/providers/Microsoft.Insights/actionGroups/test"
  }
}

run "closed_even_when_public_ip_is_known" {
  command = plan
  assert {
    condition     = azurerm_web_application_firewall_policy.application.policy_settings[0].mode == "Prevention" && anytrue([for rule in azurerm_web_application_firewall_policy.application.custom_rules : rule.name == "PublicationDisabled" && rule.action == "Block" && rule.priority == 1])
    error_message = "Disabled publication must block direct public-IP requests at the WAF."
  }
  assert {
    condition     = one(azurerm_application_gateway.regional.backend_http_settings).port == 8080 && one(azurerm_application_gateway.regional.backend_address_pool).ip_addresses == toset(["10.40.20.20"])
    error_message = "Application Gateway must use the private ILB backend."
  }
}

run "open_only_removes_publication_block" {
  command = plan
  variables { enable_public_traffic = true }
  assert {
    condition     = length(azurerm_web_application_firewall_policy.application.custom_rules) == 1 && alltrue([for rule in azurerm_web_application_firewall_policy.application.custom_rules : rule.name == "RejectUnknownHost" && rule.action == "Block"])
    error_message = "Publishing must preserve host filtering and managed WAF rules."
  }
}
