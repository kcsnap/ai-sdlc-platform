resource "azurerm_service_plan" "this" {
  name                = "${var.name}-plan"
  resource_group_name = var.resource_group_name
  location            = var.location
  os_type             = "Linux"
  sku_name            = "Y1"
  tags                = var.tags
}

resource "azurerm_linux_function_app" "this" {
  name                       = var.name
  resource_group_name        = var.resource_group_name
  location                   = var.location
  service_plan_id            = azurerm_service_plan.this.id
  storage_account_name       = var.storage_account_name
  storage_account_access_key = var.storage_account_access_key
  tags                       = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [var.user_assigned_identity_id]
  }

  site_config {
    application_stack {
      dotnet_version              = "8.0"
      use_dotnet_isolated_runtime = true
    }
  }

  app_settings = merge(
    {
      APPLICATIONINSIGHTS_CONNECTION_STRING = var.app_insights_connection_string
      AZURE_CLIENT_ID                       = var.managed_identity_client_id
      KeyVaultUri                           = var.key_vault_uri
      AuditStorageAccountName               = var.audit_storage_account_name
      FUNCTIONS_EXTENSION_VERSION           = "~4"
      WEBSITE_RUN_FROM_PACKAGE              = "1"
    },
    var.app_settings
  )
}
