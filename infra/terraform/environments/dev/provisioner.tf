# Standalone Yorrixx.Provisioner — Azure Function (Flex Consumption) (ADR-XR-0001 / G3, re-cut from
# Container App to match the platform stack and reuse the function-app module).
#
# DRAFT for STAGE 2 apply. Nothing here is applied on merge (no Terraform automation in CI). The
# subscription-scoped Contributor / User Access Administrator / Cost Management Reader + Graph
# Application.ReadWrite.OwnedBy grants are deliberately NOT here — they are the human-gated
# scripts/provisioner-identity-grants.ps1, kept out of infra apply on purpose.
#
# ⚠️ STAGE-2 CLEANUP: the function-app module currently HARDCODES orchestrator-specific app settings
#    (AnthropicApiKey/GitHubPat/GitHubWebhookSecret Key Vault refs, AnthropicModel, AuditStorageAccountName).
#    Those are irrelevant to the provisioner. Before apply, parameterize the module to make those caller-
#    supplied (move them to the orchestrator's module call in main.tf) so the provisioner doesn't carry
#    broken/unused KV refs — a small module refactor intentionally deferred (it touches the live
#    orchestrator deploy; out of this prep PR's scope). Flagged for the human.

# Dedicated identity — the provisioner is the only component holding cloud-write creds; it MUST be
# separable from the shared agent identity (id-aisdlc-dev), which must never hold subscription write.
resource "azurerm_user_assigned_identity" "provisioner" {
  name                = "id-aisdlc-provisioner-${var.environment}"
  resource_group_name = azurerm_resource_group.this.name
  location            = var.location
  tags                = local.tags
}

# Dedicated host storage for the provisioner Function: AzureWebJobsStorage + the provision-status table +
# the provision/deprovision work queues all live here (accessed via the provisioner's managed identity).
module "provisioner_storage" {
  source              = "../../modules/storage-account"
  name                = "stprov${var.environment}${var.suffix}"
  resource_group_name = azurerm_resource_group.this.name
  location            = var.location
  tags                = local.tags
  containers          = ["deployments"] # FC1 deployment container (see function-app module)
}

# Functions host needs blob + queue + table on its own storage; the provision-status table and the work
# queues ride the same account/identity.
resource "azurerm_role_assignment" "provisioner_storage_blob" {
  scope                = module.provisioner_storage.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.provisioner.principal_id
}

resource "azurerm_role_assignment" "provisioner_storage_queue" {
  scope                = module.provisioner_storage.id
  role_definition_name = "Storage Queue Data Contributor"
  principal_id         = azurerm_user_assigned_identity.provisioner.principal_id
}

resource "azurerm_role_assignment" "provisioner_storage_table" {
  scope                = module.provisioner_storage.id
  role_definition_name = "Storage Table Data Contributor"
  principal_id         = azurerm_user_assigned_identity.provisioner.principal_id
}

# The provisioner Function App (FC1 / dotnet-isolated 8.0), reusing the platform's function-app module.
module "provisioner_function" {
  source              = "../../modules/function-app"
  name                = "func-aisdlc-prov-${var.environment}-${var.suffix}"
  resource_group_name = azurerm_resource_group.this.name
  location            = var.function_app_location
  tags                = local.tags

  storage_account_name          = module.provisioner_storage.name
  storage_account_blob_endpoint = module.provisioner_storage.primary_blob_endpoint

  app_insights_connection_string = module.app_insights.connection_string
  user_assigned_identity_id      = azurerm_user_assigned_identity.provisioner.id
  managed_identity_client_id     = azurerm_user_assigned_identity.provisioner.client_id
  key_vault_uri                  = module.key_vault.vault_uri
  audit_storage_account_name     = module.provisioner_storage.name # provisioner doesn't audit; harmless

  # Provisioner-specific NON-secret settings. Secrets (Provisioner__InboundKey, Hosting__ClerkSecretKey,
  # Platform__CallbackKey) are set out-of-band from Key Vault at deploy time — see the runbook.
  app_settings = {
    "Provisioner__StorageAccountName" = module.provisioner_storage.name
    "Hosting__SubscriptionId"         = var.subscription_id
    "Hosting__ResourceGroup"          = azurerm_resource_group.this.name
  }
}

output "provisioner_base_url" {
  description = "Provisioner Function base URL — the platform sets this as ProvisionerUrl at G4 wiring."
  value       = "https://${module.provisioner_function.default_hostname}"
}
