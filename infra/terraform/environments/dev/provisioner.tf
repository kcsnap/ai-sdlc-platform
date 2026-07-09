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

  # The function-app module owns the FULL appSettings array, so the secrets live here too (KV-reference
  # form, resolved via the provisioner UAMI's Key Vault access policy) — otherwise a terraform apply would
  # wipe settings that were set out-of-band.
  app_settings = {
    "Provisioner__StorageAccountName" = module.provisioner_storage.name
    "Hosting__SubscriptionId"         = var.subscription_id
    # Per-app COMPUTE target — a yorrixx-owned RG, NOT this stack's rg-aisdlc-dev (and not the shared rg-yorrixx-dev).
    "Hosting__ResourceGroup" = "rg-yorrixx-userapps-dev"

    # G4 — identity + platform-callback wiring.
    "Hosting__TenantId"                   = var.tenant_id
    "Hosting__ApiManagedIdentityClientId" = azurerm_user_assigned_identity.provisioner.client_id
    # Callback to the orchestrator (Call 2). Hostname from the name expression to avoid a module cycle.
    "Platform__ProvisionResultUrl" = "https://func-aisdlc-${var.environment}-${var.suffix}.azurewebsites.net/api/provision-result"

    # Secrets (KV references, resolved via the provisioner UAMI's Key Vault access policy).
    "Provisioner__InboundKey" = "@Microsoft.KeyVault(SecretUri=${module.key_vault.vault_uri}secrets/ProvisionerInboundKey)"
    "Hosting__ClerkSecretKey" = "@Microsoft.KeyVault(SecretUri=${module.key_vault.vault_uri}secrets/ClerkClientSecret)"
    "Platform__CallbackKey"   = "@Microsoft.KeyVault(SecretUri=${module.key_vault.vault_uri}secrets/ProvisionResultCallbackKey)"

    # Shared user-app platform stack (yorrixx-owned, rg-yorrixx-dev) that the provisioner provisions INTO.
    # Sourced from yorrixx-app's live in-process Hosting config (G4 finalize) so the relocated provisioner
    # is a true drop-in.
    "Hosting__Location"                    = "westeurope" # per-app compute region (uksouth F1 quota = 0)
    "Hosting__UserdataCosmosAccountName"   = "cosmos-yorrixx-dev-userdata-96mj"
    "Hosting__UserdataCosmosEndpoint"      = "https://cosmos-yorrixx-dev-userdata-96mj.documents.azure.com:443/"
    "Hosting__UserdataCosmosResourceGroup" = "rg-yorrixx-dev"
    "Hosting__UserdataCosmosDatabase"      = "userapps"
    "Hosting__KeyVaultName"                = "kv-yorrixx-dev"
    "Hosting__KeyVaultResourceGroup"       = "rg-yorrixx-dev"
    "Hosting__AppInsightsWorkspaceId"      = "/subscriptions/${var.subscription_id}/resourceGroups/rg-yorrixx-dev/providers/Microsoft.OperationalInsights/workspaces/log-yorrixx-dev"
    "Hosting__ClerkAuthority"              = "https://mint-boar-35.clerk.accounts.dev"
    "Hosting__ClerkPublishableKeyFallback" = "pk_test_bWludC1ib2FyLTM1LmNsZXJrLmFjY291bnRzLmRldiQ=" # publishable (non-secret)
    "Hosting__UserAppRepoOwner"            = "yorrixx-apps"
    "Hosting__UserAppRepoNamePrefix"       = "user-app"
    "Hosting__UserAppDefaultBranch"        = "main"
  }
}

# Ramp prep — same FC1 soft-alwaysReady failure mode as the intake (see intake_keepwarm in main.tf):
# a deallocated provisioner turns Call 1 into a >60s lazy re-specialization and fails the build's
# provision step (ProvisionerClient timeout). Ping the REAL /health function (routePrefix is "" on the
# provisioner — no /api) every 5 minutes so callers land on a warm worker.
resource "azurerm_application_insights_standard_web_test" "provisioner_keepwarm" {
  name                    = "ping-provisioner-${var.environment}"
  resource_group_name     = azurerm_resource_group.this.name
  location                = var.location # must match the App Insights component's region
  application_insights_id = module.app_insights.id
  enabled                 = true
  frequency               = 300
  timeout                 = 120 # generous so the ping itself can absorb a full cold start
  retry_enabled           = true
  geo_locations           = ["emea-nl-ams-azr", "emea-gb-db3-azr"]

  request {
    url = "https://func-aisdlc-prov-${var.environment}-${var.suffix}.azurewebsites.net/health"
  }

  validation_rules {
    expected_status_code = 200
  }

  tags = local.tags
}

output "provisioner_base_url" {
  description = "Provisioner Function base URL — the platform sets this as ProvisionerUrl at G4 wiring."
  value       = "https://${module.provisioner_function.default_hostname}"
}
