mock_provider "azurerm" {}
mock_provider "kubernetes" {}
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
override_data {
  target = data.terraform_remote_state.configuration
  values = {
    "outputs" : {
      "configuration" : {
        "identity" : {
          "name" : "config-identity",
          "version" : "test-version"
        },
        "claims" : {
          "name" : "config-claims",
          "version" : "test-version"
        },
        "adjusters" : {
          "name" : "config-adjusters",
          "version" : "test-version"
        },
        "location" : {
          "name" : "config-location",
          "version" : "test-version"
        },
        "dispatch" : {
          "name" : "config-dispatch",
          "version" : "test-version"
        },
        "policy" : {
          "name" : "config-policy",
          "version" : "test-version"
        },
        "sla" : {
          "name" : "config-sla",
          "version" : "test-version"
        },
        "documents" : {
          "name" : "config-documents",
          "version" : "test-version"
        },
        "operations" : {
          "name" : "config-operations",
          "version" : "test-version"
        },
        "gateway" : {
          "name" : "config-gateway",
          "version" : "test-version"
        }
      }
    }
  }
}
override_data {
  target = data.azurerm_kubernetes_cluster.regional
  values = { kube_config = [{ host = "https://private.test:443", cluster_ca_certificate = "ZmFrZQ==" }] }
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
  data_state_key = "data-centralus.tfstate"
  base_domain    = "claims.example.com"
  release_id     = "test-release"
  service_images = {
    "identity" : "test.azurecr.io/identity:r1",
    "claims" : "test.azurecr.io/claims:r1",
    "adjusters" : "test.azurecr.io/adjusters:r1",
    "location" : "test.azurecr.io/location:r1",
    "dispatch" : "test.azurecr.io/dispatch:r1",
    "policy" : "test.azurecr.io/policy:r1",
    "sla" : "test.azurecr.io/sla:r1",
    "documents" : "test.azurecr.io/documents:r1",
    "operations" : "test.azurecr.io/operations:r1",
    "gateway" : "test.azurecr.io/gateway:r1"
  }
  migration_images = {
    "identity" : "test.azurecr.io/identity-migration:r1",
    "claims" : "test.azurecr.io/claims-migration:r1",
    "adjusters" : "test.azurecr.io/adjusters-migration:r1",
    "location" : "test.azurecr.io/location-migration:r1",
    "dispatch" : "test.azurecr.io/dispatch-migration:r1",
    "policy" : "test.azurecr.io/policy-migration:r1",
    "sla" : "test.azurecr.io/sla-migration:r1",
    "documents" : "test.azurecr.io/documents-migration:r1",
    "operations" : "test.azurecr.io/operations-migration:r1"
  }
  frontend_images = {
    "insured-app" : "test.azurecr.io/insured-app:r1",
    "adjuster-app" : "test.azurecr.io/adjuster-app:r1",
    "control-tower" : "test.azurecr.io/control-tower:r1"
  }
  proxy_image     = "test.azurecr.io/nginx:r1"
  collector_image = "test.azurecr.io/collector:r1"
}

run "complete_private_workload_inventory" {
  command = plan
  variables { environment = "qa" }
  assert {
    condition     = length(kubernetes_job_v1.migration) == 9 && length(kubernetes_deployment_v1.backend) == 10 && length(kubernetes_deployment_v1.frontend) == 3
    error_message = "Every backend needs an explicit migration and all three consoles must be hosted."
  }
  assert {
    condition     = alltrue([for deployment in values(kubernetes_deployment_v1.backend) : deployment.spec[0].replicas == "1" && deployment.spec[0].template[0].spec[0].security_context[0].run_as_non_root]) && kubernetes_service_v1.ingress.metadata[0].annotations["service.beta.kubernetes.io/azure-load-balancer-internal"] == "true"
    error_message = "Do not expose a public Kubernetes LB or claim unsupported backend scale-out."
  }
  assert {
    condition     = kubernetes_service_v1.backend["claims"].metadata[0].name == "claims-service" && kubernetes_service_v1.backend["claims"].spec[0].port[0].port == 80 && kubernetes_service_v1.backend["claims"].spec[0].port[0].target_port == "8080"
    error_message = "Existing YARP discovery names and the production container port must remain compatible."
  }
  assert {
    condition     = anytrue([for entry in kubernetes_deployment_v1.backend["claims"].spec[0].template[0].spec[0].container[0].env : entry.name == "OTEL_RESOURCE_ATTRIBUTES" && strcontains(entry.value, "deployment.environment=qa,")])
    error_message = "Workload telemetry must identify its environment instead of mixing QA with production/reference."
  }
}
run "reject_mutable_latest_image" {
  command = plan
  variables { proxy_image = "nginxinc/nginx-unprivileged:latest" }
  expect_failures = [terraform_data.images]
}
