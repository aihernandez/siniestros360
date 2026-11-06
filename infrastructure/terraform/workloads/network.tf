resource "kubernetes_network_policy_v1" "egress" {
  metadata {
    name      = "default-deny-and-required-egress"
    namespace = var.namespace
  }
  spec {
    pod_selector {}
    policy_types = ["Ingress", "Egress"]
    egress {
      to {
        namespace_selector {
          match_labels = { "kubernetes.io/metadata.name" = "kube-system" }
        }
      }
      ports {
        protocol = "UDP"
        port     = "53"
      }
      ports {
        protocol = "TCP"
        port     = "53"
      }
    }
    egress {
      to {
        pod_selector {}
      }
      ports {
        protocol = "TCP"
        port     = "80"
      }
      ports {
        protocol = "TCP"
        port     = "8080"
      }
      ports {
        protocol = "TCP"
        port     = "4317"
      }
      ports {
        protocol = "TCP"
        port     = "4318"
      }
    }
    egress {
      to {
        ip_block { cidr = local.region.subnets["postgres"].cidr }
      }
      ports {
        protocol = "TCP"
        port     = "5432"
      }
    }
    egress {
      # NAT is not an FQDN firewall; a controlled egress proxy/firewall is a separate production decision.
      to {
        ip_block { cidr = "0.0.0.0/0" }
      }
      ports {
        protocol = "TCP"
        port     = "443"
      }
      ports {
        protocol = "TCP"
        port     = "5671"
      }
    }
  }
  depends_on = [kubernetes_namespace_v1.application]
}

resource "kubernetes_network_policy_v1" "backend" {
  metadata {
    name      = "backend-from-gateway"
    namespace = var.namespace
  }
  spec {
    pod_selector { match_labels = { role = "backend" } }
    policy_types = ["Ingress"]
    ingress {
      from {
        pod_selector { match_labels = { app = "gateway" } }
      }
      ports {
        protocol = "TCP"
        port     = "8080"
      }
    }
  }
  depends_on = [kubernetes_namespace_v1.application]
}

resource "kubernetes_network_policy_v1" "gateway" {
  metadata {
    name      = "gateway-from-ingress"
    namespace = var.namespace
  }
  spec {
    pod_selector { match_labels = { role = "gateway" } }
    policy_types = ["Ingress"]
    ingress {
      from {
        pod_selector { match_labels = { app = "private-ingress" } }
      }
      ports {
        protocol = "TCP"
        port     = "8080"
      }
    }
  }
  depends_on = [kubernetes_namespace_v1.application]
}

resource "kubernetes_network_policy_v1" "frontend" {
  metadata {
    name      = "frontend-from-ingress"
    namespace = var.namespace
  }
  spec {
    pod_selector { match_labels = { role = "frontend" } }
    policy_types = ["Ingress"]
    ingress {
      from {
        pod_selector { match_labels = { app = "private-ingress" } }
      }
      ports {
        protocol = "TCP"
        port     = "8080"
      }
    }
  }
  depends_on = [kubernetes_namespace_v1.application]
}

resource "kubernetes_network_policy_v1" "telemetry" {
  metadata {
    name      = "telemetry-from-local-workloads"
    namespace = var.namespace
  }
  spec {
    pod_selector { match_labels = { app = "telemetry" } }
    policy_types = ["Ingress"]
    ingress {
      from {
        pod_selector {}
      }
      ports {
        protocol = "TCP"
        port     = "4317"
      }
      ports {
        protocol = "TCP"
        port     = "4318"
      }
    }
  }
  depends_on = [kubernetes_namespace_v1.application]
}

resource "kubernetes_network_policy_v1" "entry" {
  metadata {
    name      = "ingress-from-appgateway-only"
    namespace = var.namespace
  }
  spec {
    pod_selector { match_labels = { app = "private-ingress" } }
    policy_types = ["Ingress"]
    ingress {
      from {
        ip_block { cidr = local.region.subnets["ingress"].cidr }
      }
      from {
        ip_block { cidr = "168.63.129.16/32" }
      }
      ports {
        protocol = "TCP"
        port     = "8080"
      }
    }
  }
  depends_on = [kubernetes_namespace_v1.application]
}
