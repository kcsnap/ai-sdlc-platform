variable "name" {
  description = "Key Vault name (3-24 chars)"
  type        = string
}

variable "resource_group_name" {
  type = string
}

variable "location" {
  type = string
}

variable "tenant_id" {
  type = string
}

variable "tags" {
  type    = map(string)
  default = {}
}

variable "secret_reader_principal_ids" {
  description = "Object IDs granted get/list on secrets (e.g. managed identity)"
  type        = list(string)
  default     = []
}

variable "secret_officer_principal_ids" {
  description = "Object IDs granted full secret CRUD (e.g. deployment service principal)"
  type        = list(string)
  default     = []
}
