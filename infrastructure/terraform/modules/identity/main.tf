resource "random_uuid" "scope" {}
resource "random_uuid" "role" { for_each = var.roles }
resource "azuread_application" "api" {
  display_name     = "${var.name}-api"
  owners           = var.owners
  sign_in_audience = "AzureADMyOrg"
  api {
    requested_access_token_version = 2
    oauth2_permission_scope {
      id                         = random_uuid.scope.result
      value                      = "access_as_user"
      type                       = "User"
      enabled                    = true
      admin_consent_display_name = "Access Siniestros360"
      admin_consent_description  = "Call the claim APIs on behalf of the signed-in user."
      user_consent_display_name  = "Access your claims"
      user_consent_description   = "Call Siniestros360 on your behalf."
    }
  }
  dynamic "app_role" {
    for_each = var.roles
    content {
      id                   = random_uuid.role[app_role.value].result
      allowed_member_types = ["User"]
      display_name         = app_role.value
      description          = "Siniestros360 ${app_role.value}"
      value                = app_role.value
      enabled              = true
    }
  }
}
resource "azuread_application_identifier_uri" "api" {
  application_id = azuread_application.api.id
  identifier_uri = "api://${azuread_application.api.client_id}"
}
resource "azuread_service_principal" "api" {
  client_id = azuread_application.api.client_id
  owners    = var.owners
}
resource "azuread_application" "spa" {
  display_name     = "${var.name}-spa"
  owners           = var.owners
  sign_in_audience = "AzureADMyOrg"
  single_page_application { redirect_uris = var.redirect_uris }
  required_resource_access {
    resource_app_id = azuread_application.api.client_id
    resource_access {
      id   = random_uuid.scope.result
      type = "Scope"
    }
  }
}
resource "azuread_service_principal" "spa" {
  client_id = azuread_application.spa.client_id
  owners    = var.owners
}
resource "azuread_service_principal_delegated_permission_grant" "api" {
  service_principal_object_id          = azuread_service_principal.spa.object_id
  resource_service_principal_object_id = azuread_service_principal.api.object_id
  claim_values                         = ["access_as_user"]
}
resource "azuread_application" "native" {
  count            = length(var.native_redirect_uris) > 0 ? 1 : 0
  display_name     = "${var.name}-native"
  owners           = var.owners
  sign_in_audience = "AzureADMyOrg"
  public_client { redirect_uris = var.native_redirect_uris }
  required_resource_access {
    resource_app_id = azuread_application.api.client_id
    resource_access {
      id   = random_uuid.scope.result
      type = "Scope"
    }
  }
}
resource "azuread_service_principal" "native" {
  count     = length(var.native_redirect_uris) > 0 ? 1 : 0
  client_id = azuread_application.native[0].client_id
  owners    = var.owners
}
resource "azuread_service_principal_delegated_permission_grant" "native" {
  count                                = length(var.native_redirect_uris) > 0 ? 1 : 0
  service_principal_object_id          = azuread_service_principal.native[0].object_id
  resource_service_principal_object_id = azuread_service_principal.api.object_id
  claim_values                         = ["access_as_user"]
}
output "registration" {
  description = "Public application identifiers and scope; roles/users, domain verification and customer sign-up flows still require tenant policy decisions."
  value       = { api_client_id = azuread_application.api.client_id, spa_client_id = azuread_application.spa.client_id, native_client_id = try(azuread_application.native[0].client_id, null), scope = "api://${azuread_application.api.client_id}/access_as_user" }
}
