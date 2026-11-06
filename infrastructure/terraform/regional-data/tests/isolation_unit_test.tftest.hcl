mock_provider "azurerm" {
  mock_data "azurerm_storage_account" {
    defaults = { id = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/teststorage" }
  }
  mock_data "azurerm_user_assigned_identity" {
    defaults = { principal_id = "00000000-0000-0000-0000-000000000001" }
  }
}
mock_provider "postgresql" {}
mock_provider "random" {
  override_during = plan
  mock_resource "random_password" { defaults = { result = "fake-test-only-password" } }
}
override_data {
  target = data.terraform_remote_state.platform
  values = {
    "outputs" : {
      "tenant_id" : "00000000-0000-0000-0000-000000000001",
      "regions" : {
        "centralus" : {
          "name" : "s360-test-centralus",
          "location" : "centralus",
          "resource_group_name" : "rg-test",
          "cluster_name" : "aks-test",
          "cluster_dns_ip" : "172.21.0.10",
          "postgres_fqdn" : "test.postgres.database.azure.com",
          "database_names" : {
            "identity" : "siniestros360_identity",
            "claims" : "siniestros360_claims",
            "adjusters" : "siniestros360_adjusters",
            "location" : "siniestros360_location",
            "dispatch" : "siniestros360_dispatch",
            "policy" : "siniestros360_policy",
            "sla" : "siniestros360_sla",
            "documents" : "siniestros360_documents",
            "operations" : "siniestros360_operations"
          },
          "key_vault_name" : "kv-test",
          "key_vault_id" : "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/test/providers/Microsoft.KeyVault/vaults/kv-test",
          "servicebus_namespace" : "sb-test",
          "storage_account_name" : "teststorage",
          "storage_blob_endpoint" : "https://teststorage.blob.core.windows.net/",
          "service_identities" : {
            "identity" : {
              "client_id" : "00000000-0000-0000-0000-000000000001",
              "principal_id" : "00000000-0000-0000-0000-000000000001"
            },
            "claims" : {
              "client_id" : "00000000-0000-0000-0000-000000000001",
              "principal_id" : "00000000-0000-0000-0000-000000000001"
            },
            "adjusters" : {
              "client_id" : "00000000-0000-0000-0000-000000000001",
              "principal_id" : "00000000-0000-0000-0000-000000000001"
            },
            "location" : {
              "client_id" : "00000000-0000-0000-0000-000000000001",
              "principal_id" : "00000000-0000-0000-0000-000000000001"
            },
            "dispatch" : {
              "client_id" : "00000000-0000-0000-0000-000000000001",
              "principal_id" : "00000000-0000-0000-0000-000000000001"
            },
            "policy" : {
              "client_id" : "00000000-0000-0000-0000-000000000001",
              "principal_id" : "00000000-0000-0000-0000-000000000001"
            },
            "sla" : {
              "client_id" : "00000000-0000-0000-0000-000000000001",
              "principal_id" : "00000000-0000-0000-0000-000000000001"
            },
            "documents" : {
              "client_id" : "00000000-0000-0000-0000-000000000001",
              "principal_id" : "00000000-0000-0000-0000-000000000001"
            },
            "operations" : {
              "client_id" : "00000000-0000-0000-0000-000000000001",
              "principal_id" : "00000000-0000-0000-0000-000000000001"
            },
            "gateway" : {
              "client_id" : "00000000-0000-0000-0000-000000000001",
              "principal_id" : "00000000-0000-0000-0000-000000000001"
            },
            "telemetry" : {
              "client_id" : "00000000-0000-0000-0000-000000000001",
              "principal_id" : "00000000-0000-0000-0000-000000000001"
            }
          },
          "ingress_backend_ip" : "10.40.20.20",
          "subnets" : {
            "postgres" : {
              "id" : "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/test/providers/Microsoft.Network/virtualNetworks/test/subnets/postgres",
              "cidr" : "10.40.17.0/24"
            },
            "ingress" : {
              "id" : "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/test/providers/Microsoft.Network/virtualNetworks/test/subnets/ingress",
              "cidr" : "10.40.16.0/24"
            }
          }
        }
      },
      "bootstrap_credentials" : {
        "database_admin" : {
          "centralus" : "fake-test-only-password"
        },
        "legacy_jwt" : "fake-test-only-jwt-key-at-least-32-characters",
        "telemetry" : {
          "centralus" : "InstrumentationKey=00000000-0000-0000-0000-000000000001;IngestionEndpoint=https://example.test/"
        }
      }
    }
  }
}
variables {
  subscription_id = "00000000-0000-0000-0000-000000000001"
  region_key      = "centralus"
  state = {
    "resource_group_name" : "test",
    "storage_account_name" : "test",
    "container_name" : "tfstate",
    "platform_key" : "platform.tfstate"
  }
  blob_sas_start_utc  = "2026-11-06T00:00:00Z"
  blob_sas_expiry_utc = "2026-11-20T00:00:00Z"
  tls_pfx_base64      = "ZmFrZS1wZng="
  tls_pfx_password    = "fake-password"
}

run "service_database_and_secret_boundaries" {
  command = plan
  assert {
    condition     = length(postgresql_role.service) == 9 && alltrue([for role in values(postgresql_role.service) : !role.superuser && !role.create_role && !role.create_database]) && alltrue([for grant in values(postgresql_grant.public_database) : length(grant.privileges) == 0])
    error_message = "Service credentials must not be administrators and PUBLIC must not connect to other application databases."
  }
  assert {
    condition     = length(azurerm_role_assignment.configuration_read) == 10 && alltrue([for key, grant in azurerm_role_assignment.configuration_read : endswith(grant.scope, "/secrets/config-${key}")]) && endswith(azurerm_role_assignment.tls_read.scope, "/secrets/regional-tls")
    error_message = "App and ingress identities must read only their named secrets."
  }
  assert {
    condition     = local.configuration["documents"].Storage.UseAzureBlob && !local.configuration["claims"].Database.MigrateOnStartup
    error_message = "Cloud storage and explicit migration jobs must replace local disk/startup migration behavior."
  }
}
run "reject_long_lived_compatibility_sas" {
  command = plan
  variables { blob_sas_expiry_utc = "2027-11-06T00:00:00Z" }
  expect_failures = [data.azurerm_storage_account_blob_container_sas.documents]
}
