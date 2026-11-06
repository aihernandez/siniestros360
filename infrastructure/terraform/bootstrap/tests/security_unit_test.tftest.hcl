mock_provider "azurerm" {}

variables {
  subscription_id    = "00000000-0000-0000-0000-000000000001"
  operator_object_id = "00000000-0000-0000-0000-000000000002"
  name               = "s360statetest"
  allowed_ip_cidrs   = ["203.0.113.10/32"]
}

run "protected_state" {
  command = plan
  assert {
    condition     = !azurerm_storage_account.state.shared_access_key_enabled && azurerm_storage_account.state.network_rules[0].default_action == "Deny" && azurerm_storage_account.state.blob_properties[0].versioning_enabled
    error_message = "State must reject shared keys, restrict networks and retain versions."
  }
}

run "reject_open_state_network" {
  command = plan
  variables { allowed_ip_cidrs = ["0.0.0.0/0"] }
  expect_failures = [var.allowed_ip_cidrs]
}
