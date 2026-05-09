variable "subscription_id" {
  type = string
}

variable "tenant_id" {
  type = string
}

variable "location" {
  type    = string
  default = "uksouth"
}

variable "environment" {
  type    = string
  default = "dev"
}

variable "suffix" {
  description = "Short unique suffix appended to globally-scoped resource names (last 4 chars of subscription ID)"
  type        = string
  default     = "81c0"
}

variable "deployment_principal_object_id" {
  description = "Object ID of the service principal / user running Terraform (granted Key Vault officer access)"
  type        = string
}
