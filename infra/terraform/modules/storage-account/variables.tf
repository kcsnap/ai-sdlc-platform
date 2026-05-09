variable "name" {
  description = "Storage account name (3-24 chars, lowercase alphanumeric)"
  type        = string
}

variable "resource_group_name" {
  type = string
}

variable "location" {
  type = string
}

variable "tags" {
  type    = map(string)
  default = {}
}

variable "containers" {
  description = "Blob container names to create"
  type        = list(string)
  default     = []
}

variable "tables" {
  description = "Table Storage table names to create"
  type        = list(string)
  default     = []
}
