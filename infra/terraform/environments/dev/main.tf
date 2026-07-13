terraform {
  required_version = ">= 1.6.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    azapi = {
      source  = "azure/azapi"
      version = "~> 2.0"
    }
  }
}

provider "azurerm" {
  subscription_id = var.subscription_id
  features {}
}

locals {
  tags = {
    environment = var.environment
    project     = "ai-sdlc"
    managed_by  = "terraform"
  }
}

resource "azurerm_resource_group" "this" {
  name     = "rg-aisdlc-${var.environment}"
  location = var.location
  tags     = local.tags
}

module "managed_identity" {
  source              = "../../modules/managed-identity"
  name                = "id-aisdlc-${var.environment}"
  resource_group_name = azurerm_resource_group.this.name
  location            = var.location
  tags                = local.tags
}

module "log_analytics" {
  source              = "../../modules/log-analytics"
  name                = "law-aisdlc-${var.environment}"
  resource_group_name = azurerm_resource_group.this.name
  location            = var.location
  retention_in_days   = 30
  tags                = local.tags
}

module "app_insights" {
  source                     = "../../modules/application-insights"
  name                       = "appi-aisdlc-${var.environment}"
  resource_group_name        = azurerm_resource_group.this.name
  location                   = var.location
  log_analytics_workspace_id = module.log_analytics.id
  tags                       = local.tags
}

module "host_storage" {
  source              = "../../modules/storage-account"
  name                = "sthost${var.environment}${var.suffix}"
  resource_group_name = azurerm_resource_group.this.name
  location            = var.location
  tags                = local.tags
  containers          = ["deployments"]
}

module "audit_storage" {
  source              = "../../modules/storage-account"
  name                = "staisdlc${var.environment}${var.suffix}"
  resource_group_name = azurerm_resource_group.this.name
  location            = var.location
  tags                = local.tags
  containers          = ["prompts", "context"]
  tables              = ["AuditEvents"]
}

module "key_vault" {
  source              = "../../modules/key-vault"
  name                = "kv-aisdlc-${var.suffix}"
  resource_group_name = azurerm_resource_group.this.name
  location            = var.location
  tenant_id           = var.tenant_id
  tags                = local.tags

  secret_reader_principal_ids  = [module.managed_identity.principal_id, azurerm_user_assigned_identity.provisioner.principal_id]
  secret_officer_principal_ids = [var.deployment_principal_object_id]
}

# Storage Data Contributor on audit storage for the managed identity
resource "azurerm_role_assignment" "audit_storage_blob" {
  scope                = module.audit_storage.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = module.managed_identity.principal_id
}

resource "azurerm_role_assignment" "audit_storage_table" {
  scope                = module.audit_storage.id
  role_definition_name = "Storage Table Data Contributor"
  principal_id         = module.managed_identity.principal_id
}

# Storage roles for function app host storage (Durable Functions needs blob, queue, and table)
resource "azurerm_role_assignment" "host_storage_blob" {
  scope                = module.host_storage.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = module.managed_identity.principal_id
}

resource "azurerm_role_assignment" "host_storage_queue" {
  scope                = module.host_storage.id
  role_definition_name = "Storage Queue Data Contributor"
  principal_id         = module.managed_identity.principal_id
}

resource "azurerm_role_assignment" "host_storage_table" {
  scope                = module.host_storage.id
  role_definition_name = "Storage Table Data Contributor"
  principal_id         = module.managed_identity.principal_id
}

module "function_app" {
  source              = "../../modules/function-app"
  name                = "func-aisdlc-${var.environment}-${var.suffix}"
  resource_group_name = azurerm_resource_group.this.name
  location            = var.function_app_location
  tags                = local.tags

  storage_account_name          = module.host_storage.name
  storage_account_blob_endpoint = module.host_storage.primary_blob_endpoint

  app_insights_connection_string = module.app_insights.connection_string
  user_assigned_identity_id      = module.managed_identity.id
  managed_identity_client_id     = module.managed_identity.client_id
  key_vault_uri                  = module.key_vault.vault_uri
  app_settings = {
    # Org swept by ReconciliationSweepFunction for stranded ai-sdlc:bootstrap issues.
    ReconciliationOrg = "yorrixx-apps"

    # App-specific settings, moved out of the function-app module so it stays cleanly reusable (the
    # provisioner must not inherit these). Values are byte-identical to the module's prior hardcoded
    # ones → the orchestrator's redeploy is inert.
    AnthropicModel          = "claude-haiku-4-5-20251001"
    AnthropicApiKey         = "@Microsoft.KeyVault(SecretUri=${module.key_vault.vault_uri}secrets/AnthropicApiKey)"
    GitHubPat               = "@Microsoft.KeyVault(SecretUri=${module.key_vault.vault_uri}secrets/GitHubPat)"
    GitHubWebhookSecret     = "@Microsoft.KeyVault(SecretUri=${module.key_vault.vault_uri}secrets/GitHubWebhookSecret)"
    AuditStorageAccountName = module.audit_storage.name

    # G4 — platform → provisioner wiring. The orchestrator presents ProvisionerInboundKey as the
    # X-Platform-Provision-Key header on outbound /provision calls, and validates the inbound
    # /api/provision-result callback against ProvisionResultCallbackKey. Provisioner hostname is built from
    # the name expression (not module.provisioner_function output) to avoid a module dependency cycle.
    ProvisionerUrl             = "https://func-aisdlc-prov-${var.environment}-${var.suffix}.azurewebsites.net"
    ProvisionerInboundKey      = "@Microsoft.KeyVault(SecretUri=${module.key_vault.vault_uri}secrets/ProvisionerInboundKey)"
    ProvisionResultCallbackKey = "@Microsoft.KeyVault(SecretUri=${module.key_vault.vault_uri}secrets/ProvisionResultCallbackKey)"

    # G5-SEC — inbound auth on /api/builds (CreateBuildFunction validates X-Platform-Build-Key against
    # this; unset means validation is SKIPPED). yorrixx-app presents the same secret at G6.
    PlatformBuildKey = "@Microsoft.KeyVault(SecretUri=${module.key_vault.vault_uri}secrets/PlatformBuildKey)"

    # G6 P1 — outbound auth on the Yorrixx status/runtime/verification callbacks: SendCallbackAsync presents
    # this as X-Yorrixx-Admin-Key on {CallbackBaseUrl}/apps/{appId}/{kind}. Unset ⇒ the header is silently
    # omitted and every callback 401s (the G6 cutover failure).
    YorrixxAdminKey = "@Microsoft.KeyVault(SecretUri=${module.key_vault.vault_uri}secrets/YorrixxAdminKey)"

    # F1 — review gate dev convenience: auto-approve ready-for-review → live WITHOUT owner signoff.
    # Code default (unset) is the gate ON; dev keeps the old auto-publish EXPLICITLY. Never set in prod.
    AutoApproveReview = "true"

    # Ramp prep — the AnthropicRateLimiter semaphore is APP-WIDE (code default 2): at 5 concurrent
    # builds every agent stage queues behind two slots. 6 = 5 parallel builds + one slot of headroom;
    # the existing 429 exponential backoff still yields if the API-side limit is lower.
    AnthropicMaxConcurrentRequests = "6"

    # Cost telemetry (proof w1proof2): CostEmittingModelProvider is INERT unless YorrixxApiBase +
    # YorrixxAdminKey are both set — this was never configured, so no build ever emitted cost.
    # No trailing /v1/admin: the emitter appends /v1/admin/apps/{appId}/cost itself.
    YorrixxApiBase = "https://ca-yorrixx-dev-api.proudpebble-018b8327.uksouth.azurecontainerapps.io"

    # Ramp wave-1 fix — activate the template-first Static path (#193/#195 shipped it env-gated OFF).
    # With the profile stamped Static, a fresh Static build fills a pre-built template instead of
    # generating markup from scratch (cheaper, and structurally can't invent literal emails — the
    # templates already carry __CONTACT_EMAIL__). Falls back to the Code Implementer on any failure.
    StaticTemplateFirst = "true"
  }
}

# G6 flip-#2 fix — keep the build-intake's http trigger group warm. FC1 alwaysReady is a SOFT target: the
# platform deallocated the warm instance overnight (2026-07-05) and the canary's POST /api/builds hit a
# >100s lazy re-specialization despite alwaysReady http=1. This availability ping executes a REAL
# http-group function (/api/health — the site root "/" is front-end-served and proves nothing) every
# 5 minutes from two locations: worst case after a platform recycle, one ping absorbs the cold start and
# callers land on a warm worker. Doubles as the first truthful uptime signal for the intake.
resource "azurerm_application_insights_standard_web_test" "intake_keepwarm" {
  name                    = "ping-builds-intake-${var.environment}"
  resource_group_name     = azurerm_resource_group.this.name
  location                = var.location # must match the App Insights component's region
  application_insights_id = module.app_insights.id
  enabled                 = true
  frequency               = 300
  timeout                 = 120 # generous so the ping itself can absorb a full cold start
  retry_enabled           = true
  geo_locations           = ["emea-nl-ams-azr", "emea-gb-db3-azr"]

  request {
    url = "https://func-aisdlc-${var.environment}-${var.suffix}.azurewebsites.net/api/health"
  }

  validation_rules {
    expected_status_code = 200
  }

  tags = local.tags
}
