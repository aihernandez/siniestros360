mock_provider "azurerm" {}

override_data {
  target = data.terraform_remote_state.platform
  values = {
    outputs = {
      regions = {
        centralus = {
          name               = "s360-test-centralus", resource_group_name = "rg-test", location = "centralus"
          subnets            = { ingress = { id = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/test/providers/Microsoft.Network/virtualNetworks/test/subnets/ingress", cidr = "10.40.16.0/24" } }
          ingress_backend_ip = "10.40.20.20"
          edge_identity_id   = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/test/providers/Microsoft.ManagedIdentity/userAssignedIdentities/edge"
          log_workspace_id   = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/test/providers/Microsoft.OperationalInsights/workspaces/test"
          action_group_id    = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/test/providers/Microsoft.Insights/actionGroups/test"
        }
      }
    }
  }
}
override_data {
  target = data.terraform_remote_state.regional_data
  values = { outputs = { tls_secret_id = "https://example.vault.azure.net/secrets/regional-tls" } }
}

variables {
  subscription_id = "00000000-0000-0000-0000-000000000001"
  base_domain     = "claims.example.com"
  state           = { resource_group_name = "test", storage_account_name = "teststate", container_name = "tfstate", platform_key = "platform.tfstate" }
  data_keys       = { centralus = "data-centralus.tfstate" }
  geographies     = { centralus = ["GEO-NA"] }
}

run "disabled_by_default" {
  command = plan
  assert {
    condition     = !azurerm_traffic_manager_external_endpoint.primary["centralus"].enabled && !azurerm_traffic_manager_nested_endpoint.geographic["centralus"].enabled
    error_message = "DNS endpoint publication must be disabled by default."
  }
}

run "reject_unready_application" {
  command = plan
  variables { enable_public_traffic = true }
  expect_failures = [terraform_data.readiness]
}

run "reject_unready_recovery" {
  command = plan
  variables {
    recovery_targets = { unverified = { region = "centralus", fqdn = "unverified.example.com", priority = 2 } }
  }
  expect_failures = [terraform_data.readiness]
}
