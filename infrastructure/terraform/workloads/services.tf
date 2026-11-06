resource "kubernetes_deployment_v1" "backend" {
  for_each = local.backends
  metadata {
    name      = each.key
    namespace = var.namespace
  }
  spec {
    # Current SignalR and demo seed behavior are not safe to claim as multi-replica production semantics.
    replicas = 1
    selector { match_labels = { app = each.key } }
    strategy { type = "Recreate" }
    template {
      metadata {
        labels      = merge(local.pod_labels, { app = each.key, role = each.key == "gateway" ? "gateway" : "backend" })
        annotations = { "s360/configuration-version" = data.terraform_remote_state.configuration.outputs.configuration[each.key].version, "s360/release" = var.release_id }
      }
      spec {
        service_account_name = kubernetes_service_account_v1.application[each.key].metadata[0].name
        node_selector        = { "kubernetes.azure.com/agentpool" = "apps" }
        security_context {
          run_as_non_root = true
          run_as_user     = 1654
          fs_group        = 1654
          seccomp_profile { type = "RuntimeDefault" }
        }
        container {
          name  = each.key
          image = var.service_images[each.key]
          env {
            name  = "ASPNETCORE_ENVIRONMENT"
            value = "Production"
          }
          env {
            name  = "ASPNETCORE_HTTP_PORTS"
            value = "8080"
          }
          env {
            name  = "OTEL_EXPORTER_OTLP_ENDPOINT"
            value = "http://telemetry:4317"
          }
          env {
            name  = "OTEL_RESOURCE_ATTRIBUTES"
            value = "service.name=${each.key},deployment.environment=${var.environment},cloud.region=${var.region_key}"
          }
          port {
            name           = "http"
            container_port = 8080
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
          readiness_probe {
            http_get {
              path = "/health"
              port = 8080
            }
            initial_delay_seconds = 10
            period_seconds        = 10
          }
          liveness_probe {
            http_get {
              path = "/alive"
              port = 8080
            }
            initial_delay_seconds = 30
            period_seconds        = 20
          }
          startup_probe {
            http_get {
              path = "/alive"
              port = 8080
            }
            period_seconds    = 10
            failure_threshold = 30
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
  depends_on = [kubernetes_job_v1.migration, kubernetes_network_policy_v1.backend, kubernetes_manifest.configuration]
}
resource "kubernetes_service_v1" "backend" {
  for_each = local.backends
  metadata {
    name      = each.value.service_name
    namespace = var.namespace
  }
  spec {
    type     = "ClusterIP"
    selector = { app = each.key }
    port {
      port        = 80
      target_port = 8080
    }
  }
}
