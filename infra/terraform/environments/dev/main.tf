terraform {
  required_version = ">= 1.6.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.100"
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
}

module "audit_storage" {
  source              = "../../modules/storage-account"
  name                = "staisdlc${var.environment}${var.suffix}"
  resource_group_name = azurerm_resource_group.this.name
  location            = var.location
  tags                = local.tags
  containers          = ["prompts"]
  tables              = ["AuditEvents"]
}

module "key_vault" {
  source              = "../../modules/key-vault"
  name                = "kv-aisdlc-${var.suffix}"
  resource_group_name = azurerm_resource_group.this.name
  location            = var.location
  tenant_id           = var.tenant_id
  tags                = local.tags

  secret_reader_principal_ids  = [module.managed_identity.principal_id]
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

module "function_app" {
  source              = "../../modules/function-app"
  name                = "func-aisdlc-${var.environment}-${var.suffix}"
  resource_group_name = azurerm_resource_group.this.name
  location            = var.location
  tags                = local.tags

  storage_account_name       = module.host_storage.name
  storage_account_access_key = module.host_storage.primary_connection_string

  app_insights_connection_string = module.app_insights.connection_string
  user_assigned_identity_id      = module.managed_identity.id
  managed_identity_client_id     = module.managed_identity.client_id
  key_vault_uri                  = module.key_vault.vault_uri
  audit_storage_account_name     = module.audit_storage.name
}
