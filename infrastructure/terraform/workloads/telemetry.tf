resource "kubernetes_deployment_v1" "telemetry" {
  metadata {
    name      = "telemetry"
    namespace = var.namespace
  }
  spec {
    replicas = 1
    selector { match_labels = { app = "telemetry" } }
    template {
      metadata {
        labels = merge(local.pod_labels, { app = "telemetry" })
      }
      spec {
        service_account_name = kubernetes_service_account_v1.application["telemetry"].metadata[0].name
        node_selector        = { "kubernetes.azure.com/agentpool" = "apps" }
        security_context {
          run_as_non_root = true
          run_as_user     = 10001
          fs_group        = 10001
          seccomp_profile { type = "RuntimeDefault" }
        }
        container {
          name  = "collector"
          image = var.collector_image
          args  = ["--config=/conf/collector.yaml"]
          port {
            name           = "grpc"
            container_port = 4317
          }
          port {
            name           = "http"
            container_port = 4318
          }
          security_context {
            allow_privilege_escalation = false
            read_only_root_filesystem  = true
            capabilities { drop = ["ALL"] }
          }
          resources {
            requests = { cpu = "100m", memory = "256Mi" }
            limits   = { cpu = "1", memory = "512Mi" }
          }
          volume_mount {
            name       = "configuration"
            mount_path = "/conf"
            read_only  = true
          }
        }
        volume {
          name = "configuration"
          csi {
            driver            = "secrets-store.csi.k8s.io"
            read_only         = true
            volume_attributes = { secretProviderClass = "telemetry" }
          }
        }
      }
    }
  }
  depends_on = [kubernetes_manifest.telemetry_configuration]
}
resource "kubernetes_service_v1" "telemetry" {
  metadata {
    name      = "telemetry"
    namespace = var.namespace
  }
  spec {
    selector = { app = "telemetry" }
    port {
      name        = "grpc"
      port        = 4317
      target_port = 4317
    }
    port {
      name        = "http"
      port        = 4318
      target_port = 4318
    }
  }
}
output "deployment" {
  description = "Deployed regional resource names, not credentials or a production-readiness assertion."
  value       = { region = var.region_key, namespace = var.namespace, release = var.release_id, backend_service_names = { for k, v in kubernetes_service_v1.backend : k => v.metadata[0].name } }
}
