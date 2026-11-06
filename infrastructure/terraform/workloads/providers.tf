provider "azurerm" {
  subscription_id                 = var.subscription_id
  resource_provider_registrations = "none"
  storage_use_azuread             = true
  features {}
}

data "azurerm_kubernetes_cluster" "regional" {
  name                = local.region.cluster_name
  resource_group_name = local.region.resource_group_name
}
provider "kubernetes" {
  host                   = data.azurerm_kubernetes_cluster.regional.kube_config[0].host
  cluster_ca_certificate = base64decode(data.azurerm_kubernetes_cluster.regional.kube_config[0].cluster_ca_certificate)
  exec {
    api_version = "client.authentication.k8s.io/v1beta1"
    command     = "kubelogin"
    args        = ["get-token", "--login", "azurecli", "--server-id", "6dae42f8-4368-4678-94ff-3960e28e3630"]
  }
}
