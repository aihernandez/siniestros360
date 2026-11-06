resource "kubernetes_namespace_v1" "application" {
  metadata {
    name   = var.namespace
    labels = { "pod-security.kubernetes.io/enforce" = "restricted", "pod-security.kubernetes.io/enforce-version" = "latest" }
  }
}
resource "kubernetes_service_account_v1" "application" {
  for_each = merge(local.backends, { telemetry = { db_key = "", project = "Collector", service_name = "telemetry" } })
  metadata {
    name        = each.key
    namespace   = kubernetes_namespace_v1.application.metadata[0].name
    annotations = { "azure.workload.identity/client-id" = local.region.service_identities[each.key].client_id }
  }
  automount_service_account_token = false
}
resource "kubernetes_manifest" "configuration" {
  for_each = local.backends
  manifest = {
    apiVersion = "secrets-store.csi.x-k8s.io/v1"
    kind       = "SecretProviderClass"
    metadata   = { name = each.key, namespace = var.namespace }
    spec = {
      provider = "azure"
      parameters = {
        usePodIdentity = "false"
        clientID       = local.region.service_identities[each.key].client_id
        keyvaultName   = local.region.key_vault_name
        tenantId       = data.terraform_remote_state.platform.outputs.tenant_id
        objects        = yamlencode({ array = [yamlencode({ objectName = "config-${each.key}", objectType = "secret", objectAlias = "appsettings.Production.json" })] })
      }
    }
  }
  depends_on = [kubernetes_namespace_v1.application]
}
resource "kubernetes_manifest" "telemetry_configuration" {
  manifest = {
    apiVersion = "secrets-store.csi.x-k8s.io/v1"
    kind       = "SecretProviderClass"
    metadata   = { name = "telemetry", namespace = var.namespace }
    spec = { provider = "azure", parameters = {
      usePodIdentity = "false"
      clientID       = local.region.service_identities["telemetry"].client_id
      keyvaultName   = local.region.key_vault_name
      tenantId       = data.terraform_remote_state.platform.outputs.tenant_id
      objects        = yamlencode({ array = [yamlencode({ objectName = "config-telemetry", objectType = "secret", objectAlias = "collector.yaml" })] })
    } }
  }
  depends_on = [kubernetes_namespace_v1.application]
}
