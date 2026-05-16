variable "name" {
  type = string
}

variable "resource_group_name" {
  type = string
}

variable "location" {
  type = string
}

variable "storage_account_name" {
  description = "Storage account used by the Function App host (AzureWebJobsStorage)"
  type        = string
}

variable "storage_account_blob_endpoint" {
  description = "Primary blob endpoint of the host storage account (used for FC1 deployment container URL)"
  type        = string
}

variable "app_insights_connection_string" {
  type      = string
  sensitive = true
}

variable "user_assigned_identity_id" {
  description = "Resource ID of the user-assigned managed identity"
  type        = string
}

variable "managed_identity_client_id" {
  description = "Client ID of the user-assigned managed identity (written to AZURE_CLIENT_ID app setting)"
  type        = string
}

variable "key_vault_uri" {
  type = string
}

variable "audit_storage_account_name" {
  description = "Name of the audit storage account (accessed via managed identity)"
  type        = string
}

variable "app_settings" {
  description = "Additional app settings to merge in"
  type        = map(string)
  default     = {}
}

variable "tags" {
  type    = map(string)
  default = {}
}
