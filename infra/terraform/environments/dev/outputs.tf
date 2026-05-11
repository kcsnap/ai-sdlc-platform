output "function_app_hostname" {
  value = module.function_app.default_hostname
}

output "function_app_name" {
  value = module.function_app.name
}

output "key_vault_uri" {
  value = module.key_vault.vault_uri
}

output "audit_storage_account_name" {
  value = module.audit_storage.name
}

output "managed_identity_client_id" {
  value = module.managed_identity.client_id
}

output "app_insights_connection_string" {
  value     = module.app_insights.connection_string
  sensitive = true
}
