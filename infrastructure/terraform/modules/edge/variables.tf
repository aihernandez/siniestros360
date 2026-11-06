variable "certificate_secret_id" {
  type        = string
  description = "Versionless TLS certificate secret URI."
}

variable "hostnames" {
  type        = list(string)
  description = "Allowed public Host headers and probe hostname."
}

variable "region" {
  type        = object({ name = string, resource_group_name = string, location = string, subnets = map(object({ id = string, cidr = string })), ingress_backend_ip = string, edge_identity_id = string, log_workspace_id = string, action_group_id = string })
  description = "Regional platform outputs."
}

variable "tags" {
  type        = map(string)
  description = "Resource tags."
}
variable "enable_public_traffic" {
  type        = bool
  description = "Open the WAF only after the root readiness gates pass."
  default     = false
}
