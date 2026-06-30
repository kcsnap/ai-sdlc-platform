terraform {
  required_providers {
    azapi = {
      source = "azure/azapi"
    }
  }
}

locals {
  # GENERIC Functions-host settings only — every app on this module needs these. App-SPECIFIC settings
  # (Anthropic*, GitHub*, AuditStorageAccountName, …) are caller-supplied via var.app_settings so the
  # module is cleanly reusable (e.g. the provisioner must not inherit the orchestrator's Anthropic/GitHub
  # Key Vault references). keyVaultReferenceIdentity is set on the app, so KV references in caller settings
  # use the simple URI-only form: "@Microsoft.KeyVault(SecretUri=${key_vault_uri}secrets/<name>)".
  app_settings_list = [
    for k, v in merge(
      {
        APPLICATIONINSIGHTS_CONNECTION_STRING = var.app_insights_connection_string
        AZURE_CLIENT_ID                       = var.managed_identity_client_id
        KeyVaultUri                           = var.key_vault_uri
        FUNCTIONS_EXTENSION_VERSION           = "~4"
        AzureWebJobsStorage__accountName      = var.storage_account_name
        AzureWebJobsStorage__credential       = "managedidentity"
        AzureWebJobsStorage__clientId         = var.managed_identity_client_id
      },
      var.app_settings
    ) : { name = k, value = v }
  ]
}

data "azurerm_resource_group" "this" {
  name = var.resource_group_name
}

resource "azurerm_service_plan" "this" {
  name                = "${var.name}-plan"
  resource_group_name = var.resource_group_name
  location            = var.location
  os_type             = "Linux"
  sku_name            = "FC1"
  tags                = var.tags
}

resource "azapi_resource" "function_app" {
  type      = "Microsoft.Web/sites@2023-12-01"
  name      = var.name
  location  = var.location
  parent_id = data.azurerm_resource_group.this.id
  tags      = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [var.user_assigned_identity_id]
  }

  body = {
    kind = "functionapp,linux"
    properties = {
      serverFarmId = azurerm_service_plan.this.id
      functionAppConfig = {
        deployment = {
          storage = {
            type  = "blobContainer"
            value = "${var.storage_account_blob_endpoint}deployments"
            authentication = {
              type                           = "UserAssignedIdentity"
              userAssignedIdentityResourceId = var.user_assigned_identity_id
            }
          }
        }
        scaleAndConcurrency = {
          maximumInstanceCount = 100
          instanceMemoryMB     = 2048
          # One always-ready instance for the HTTP group: GitHub never retries a failed
          # webhook delivery, so a cold-start 502 on the receiver loses the event outright.
          alwaysReady = [
            {
              name          = "http"
              instanceCount = 1
            }
          ]
        }
        runtime = {
          name    = "dotnet-isolated"
          version = "8.0"
        }
      }
      keyVaultReferenceIdentity = var.user_assigned_identity_id
      siteConfig = {
        appSettings = local.app_settings_list
      }
    }
  }

  response_export_values = ["properties.defaultHostName"]
}
