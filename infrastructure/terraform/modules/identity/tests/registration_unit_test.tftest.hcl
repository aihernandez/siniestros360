mock_provider "azuread" {}
mock_provider "random" {}
variables {
  name          = "s360-test"
  owners        = []
  redirect_uris = ["https://insured.claims.example.com/"]
  roles         = ["insured"]
}

run "public_clients_and_v2_api" {
  command = plan
  assert {
    condition     = azuread_application.api.api[0].requested_access_token_version == 2 && azuread_application.api.sign_in_audience == "AzureADMyOrg" && length(azuread_application.api.app_role) == 1 && length(azuread_application.native) == 0
    error_message = "The API must expose tenant-local v2 scopes/roles and omit unspecified native callbacks."
  }
}

run "optional_native_pkce_registration" {
  command = plan
  variables { native_redirect_uris = ["msauth://com.example.insured/verified-signature"] }
  assert {
    condition     = length(azuread_application.native) == 1 && azuread_application.native[0].public_client[0].redirect_uris == toset(["msauth://com.example.insured/verified-signature"])
    error_message = "Explicit native callbacks must be registered as a public client."
  }
}

run "reject_spoofed_localhost" {
  command = plan
  variables { redirect_uris = ["http://localhost.attacker.example/callback"] }
  expect_failures = [var.redirect_uris]
}
