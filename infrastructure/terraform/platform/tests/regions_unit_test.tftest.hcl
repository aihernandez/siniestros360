mock_provider "azurerm" {
  mock_resource "azurerm_kubernetes_cluster" {
    defaults = {
      kubelet_identity = [{ client_id = "00000000-0000-0000-0000-000000000005", object_id = "00000000-0000-0000-0000-000000000006", user_assigned_identity_id = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/test/providers/Microsoft.ManagedIdentity/userAssignedIdentities/kubelet" }]
    }
  }
}
mock_provider "random" {}

variables {
  subscription_id        = "00000000-0000-0000-0000-000000000001"
  tenant_id              = "00000000-0000-0000-0000-000000000002"
  operator_object_id     = "00000000-0000-0000-0000-000000000003"
  admin_group_object_ids = ["00000000-0000-0000-0000-000000000004"]
  alert_email            = "operations@example.com"
}

run "four_independent_stamps" {
  command = plan
  assert {
    condition     = toset(keys(output.regions)) == toset(["centralus", "francecentral", "mexicocentral", "chilecentral"]) && alltrue([for region in values(output.regions) : length(region.database_names) == 9])
    error_message = "The reference needs four stamps with nine independently named service databases each."
  }
  assert {
    condition     = azurerm_container_registry.image.sku == "Premium" && !azurerm_container_registry.image.admin_enabled && !azurerm_container_registry.image.public_network_access_enabled
    error_message = "Images must use a private Premium registry without an admin password."
  }
}

run "reject_ambiguous_network" {
  command = plan
  variables {
    regions = {
      one = { location = "centralus", vnet_cidr = "10.40.1.0/16", zones = ["1"], postgres_zone_redundant = false }
    }
  }
  expect_failures = [var.regions]
}

run "development_profile" {
  command = plan
  variables {
    environment = jsondecode(file("../environments/dev/platform.tfvars.json.example")).environment
    regions     = jsondecode(file("../environments/dev/platform.tfvars.json.example")).regions
  }
  assert {
    condition     = azurerm_resource_group.global.name == "rg-s360-dev-global" && toset(keys(output.regions)) == toset(["mexicocentral"])
    error_message = "Development must create its own single-region resource names and stamp."
  }
}

run "quality_assurance_profile" {
  command = plan
  variables {
    environment = jsondecode(file("../environments/qa/platform.tfvars.json.example")).environment
    regions     = jsondecode(file("../environments/qa/platform.tfvars.json.example")).regions
  }
  assert {
    condition     = azurerm_resource_group.global.name == "rg-s360-qa-global" && toset(keys(output.regions)) == toset(["mexicocentral"])
    error_message = "QA must create its own single-region resource names and stamp."
  }
}

run "production_profile" {
  command = plan
  variables {
    environment = jsondecode(file("../environments/prod/platform.tfvars.json.example")).environment
    regions     = jsondecode(file("../environments/prod/platform.tfvars.json.example")).regions
  }
  assert {
    condition     = azurerm_resource_group.global.name == "rg-s360-prod-global" && length(output.regions) == 4
    error_message = "Production must keep its own resource prefix and the four-region scenario."
  }
}
