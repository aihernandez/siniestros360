variable "name" {
  type        = string
  description = "Application name prefix."
}

variable "native_redirect_uris" {
  type        = set(string)
  description = "Optional verified MSAL native/Capacitor callbacks. Supply package/signature-specific msauth URIs or verified HTTPS app links."
  default     = []
  validation {
    condition     = alltrue([for uri in var.native_redirect_uris : can(regex("^(https://[^/]+/|msauth(\\.[a-zA-Z0-9.-]+)?://|http://localhost(:[0-9]+)?/)", uri))])
    error_message = "Native redirects must use verified HTTPS links, registered msauth callbacks or localhost loopback."
  }
}

variable "owners" {
  type        = set(string)
  description = "Existing tenant-local owner object IDs."
}

variable "redirect_uris" {
  type        = set(string)
  description = "Registered SPA HTTPS redirect URIs; PKCE requires no client password."
  validation {
    condition     = length(var.redirect_uris) > 0 && alltrue([for uri in var.redirect_uris : can(regex("^(https://[^/]+/|http://localhost(:[0-9]+)?/)", uri))])
    error_message = "Use explicit HTTPS SPA redirects (localhost HTTP only for local development)."
  }
}

variable "roles" {
  type        = set(string)
  description = "Application roles exposed to tenant users."
}
