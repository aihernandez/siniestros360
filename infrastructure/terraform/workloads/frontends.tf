resource "kubernetes_deployment_v1" "frontend" {
  for_each = local.frontends
  metadata {
    name      = each.key
    namespace = var.namespace
  }
  spec {
    replicas = 2
    selector { match_labels = { app = each.key } }
    template {
      metadata {
        labels = { app = each.key, role = "frontend" }
      }
      spec {
        automount_service_account_token = false
        node_selector                   = { "kubernetes.azure.com/agentpool" = "apps" }
        security_context {
          run_as_non_root = true
          run_as_user     = 101
          fs_group        = 101
          seccomp_profile { type = "RuntimeDefault" }
        }
        container {
          name  = "web"
          image = var.frontend_images[each.key]
          port { container_port = 8080 }
          resources {
            requests = { cpu = "50m", memory = "64Mi" }
            limits   = { cpu = "500m", memory = "128Mi" }
          }
          security_context {
            allow_privilege_escalation = false
            read_only_root_filesystem  = true
            capabilities { drop = ["ALL"] }
          }
          readiness_probe {
            http_get {
              path = "/"
              port = 8080
            }
          }
          volume_mount {
            name       = "temporary"
            mount_path = "/tmp"
          }
        }
        volume {
          name = "temporary"
          empty_dir {}
        }
      }
    }
  }
  depends_on = [terraform_data.images, kubernetes_namespace_v1.application]
}
resource "kubernetes_service_v1" "frontend" {
  for_each = local.frontends
  metadata {
    name      = each.key
    namespace = var.namespace
  }
  spec {
    selector = { app = each.key }
    port {
      port        = 8080
      target_port = 8080
    }
  }
}
resource "kubernetes_config_map_v1" "ingress" {
  metadata {
    name      = "private-ingress"
    namespace = var.namespace
  }
  data = { "default.conf" = templatefile("${path.module}/templates/ingress.conf.tftpl", { base_domain = var.base_domain, namespace = var.namespace, dns_ip = local.region.cluster_dns_ip }) }
}
resource "kubernetes_deployment_v1" "ingress" {
  metadata {
    name      = "private-ingress"
    namespace = var.namespace
  }
  spec {
    replicas = 2
    selector { match_labels = { app = "private-ingress" } }
    template {
      metadata {
        labels      = { app = "private-ingress" }
        annotations = { "s360/configuration" = sha256(kubernetes_config_map_v1.ingress.data["default.conf"]) }
      }
      spec {
        automount_service_account_token = false
        node_selector                   = { "kubernetes.azure.com/agentpool" = "apps" }
        security_context {
          run_as_non_root = true
          run_as_user     = 101
          fs_group        = 101
          seccomp_profile { type = "RuntimeDefault" }
        }
        container {
          name  = "proxy"
          image = var.proxy_image
          port { container_port = 8080 }
          security_context {
            allow_privilege_escalation = false
            read_only_root_filesystem  = true
            capabilities { drop = ["ALL"] }
          }
          resources {
            requests = { cpu = "100m", memory = "64Mi" }
            limits   = { cpu = "1", memory = "256Mi" }
          }
          readiness_probe {
            http_get {
              path = "/health"
              port = 8080
            }
          }
          volume_mount {
            name       = "configuration"
            mount_path = "/etc/nginx/conf.d"
            read_only  = true
          }
          volume_mount {
            name       = "temporary"
            mount_path = "/tmp"
          }
        }
        volume {
          name = "configuration"
          config_map { name = kubernetes_config_map_v1.ingress.metadata[0].name }
        }
        volume {
          name = "temporary"
          empty_dir {}
        }
      }
    }
  }
  depends_on = [kubernetes_deployment_v1.backend, kubernetes_deployment_v1.frontend, terraform_data.images]
}
resource "kubernetes_service_v1" "ingress" {
  metadata {
    name      = "private-ingress"
    namespace = var.namespace
    annotations = {
      "service.beta.kubernetes.io/azure-load-balancer-internal"        = "true"
      "service.beta.kubernetes.io/azure-load-balancer-internal-subnet" = "loadbalancer"
      "service.beta.kubernetes.io/azure-load-balancer-ipv4"            = local.region.ingress_backend_ip
    }
  }
  spec {
    type                        = "LoadBalancer"
    external_traffic_policy     = "Local"
    load_balancer_source_ranges = [local.region.subnets["ingress"].cidr]
    selector                    = { app = "private-ingress" }
    port {
      port        = 8080
      target_port = 8080
    }
  }
  wait_for_load_balancer = true
}
