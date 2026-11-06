resource "kubernetes_job_v1" "migration" {
  for_each = local.services
  metadata {
    name      = "migrate-${each.key}-${substr(sha256(var.release_id), 0, 10)}"
    namespace = var.namespace
  }
  spec {
    backoff_limit = 1
    template {
      metadata {
        labels = merge(local.pod_labels, { app = "migration-${each.key}" })
      }
      spec {
        service_account_name = kubernetes_service_account_v1.application[each.key].metadata[0].name
        restart_policy       = "Never"
        node_selector        = { "kubernetes.azure.com/agentpool" = "apps" }
        security_context {
          run_as_non_root = true
          run_as_user     = 1654
          fs_group        = 1654
          seccomp_profile { type = "RuntimeDefault" }
        }
        container {
          name    = "migration"
          image   = var.migration_images[each.key]
          command = ["/app/efbundle"]
          env {
            name  = "ASPNETCORE_ENVIRONMENT"
            value = "Production"
          }
          env {
            name  = "DOTNET_BUNDLE_EXTRACT_BASE_DIR"
            value = "/tmp/.net"
          }
          security_context {
            allow_privilege_escalation = false
            read_only_root_filesystem  = true
            capabilities { drop = ["ALL"] }
          }
          resources {
            requests = { cpu = "100m", memory = "256Mi" }
            limits   = { cpu = "1", memory = "1Gi" }
          }
          volume_mount {
            name       = "configuration"
            mount_path = "/app/appsettings.Production.json"
            sub_path   = "appsettings.Production.json"
            read_only  = true
          }
          volume_mount {
            name       = "temporary"
            mount_path = "/tmp"
          }
        }
        volume {
          name = "configuration"
          csi {
            driver            = "secrets-store.csi.k8s.io"
            read_only         = true
            volume_attributes = { secretProviderClass = each.key }
          }
        }
        volume {
          name = "temporary"
          empty_dir {}
        }
      }
    }
  }
  wait_for_completion = true
  timeouts { create = "20m" }
  depends_on = [kubernetes_manifest.configuration, terraform_data.images, kubernetes_network_policy_v1.egress]
}
