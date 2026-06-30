# Standalone Yorrixx.Provisioner — Container App (ADR-XR-0001 / G3).
#
# DRAFT for STAGE 2 apply. Nothing here is applied on merge — this repo has no Terraform automation in
# CI (apply is manual; see scripts/provisioner-deploy-runbook.md). Isolated in its own file so it doesn't
# entangle with other in-flight infra changes.
#
# Deliberately NOT here: the subscription-scoped Contributor / User Access Administrator / Cost
# Management Reader and the Graph Application.ReadWrite.OwnedBy grants. Those are the highest-privilege
# grants in the system and are gated behind an explicit human/OPS run of
# scripts/provisioner-identity-grants.ps1 — kept out of infra apply on purpose.

variable "provisioner_image" {
  description = "Container image repository name for the provisioner (in the ACR below)."
  type        = string
  default     = "yorrixx-provisioner"
}

variable "provisioner_image_tag" {
  description = "Image tag to run. Managed by the deploy step at runtime; Terraform ignores drift on it."
  type        = string
  default     = "latest"
}

# Dedicated identity — the provisioner is the only component holding cloud-write creds; it MUST be
# separable from the shared agent identity (id-aisdlc-dev), which must never hold subscription write.
resource "azurerm_user_assigned_identity" "provisioner" {
  name                = "id-aisdlc-provisioner-${var.environment}"
  resource_group_name = azurerm_resource_group.this.name
  location            = var.location
  tags                = local.tags
}

# Registry for the provisioner image — managed-identity pull (no admin creds).
resource "azurerm_container_registry" "provisioner" {
  name                = "acraisdlc${var.environment}${var.suffix}"
  resource_group_name = azurerm_resource_group.this.name
  location            = var.location
  sku                 = "Basic"
  admin_enabled       = false
  tags                = local.tags
}

resource "azurerm_role_assignment" "provisioner_acr_pull" {
  scope                = azurerm_container_registry.provisioner.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.provisioner.principal_id
}

# Container Apps environment (Consumption) wired to the shared Log Analytics workspace. Container Apps
# do not consume App Service VM quota, so North Europe (var.location) is fine.
resource "azurerm_container_app_environment" "provisioner" {
  name                       = "cae-aisdlc-${var.environment}"
  resource_group_name        = azurerm_resource_group.this.name
  location                   = var.location
  log_analytics_workspace_id = module.log_analytics.id
  tags                       = local.tags
}

resource "azurerm_container_app" "provisioner" {
  name                         = "ca-aisdlc-provisioner-${var.environment}"
  resource_group_name          = azurerm_resource_group.this.name
  container_app_environment_id = azurerm_container_app_environment.provisioner.id
  revision_mode                = "Single"
  tags                         = local.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.provisioner.id]
  }

  registry {
    server   = azurerm_container_registry.provisioner.login_server
    identity = azurerm_user_assigned_identity.provisioner.id
  }

  ingress {
    # External ingress, gated by the X-Platform-Provision-Key the host validates (Provisioner__InboundKey).
    # The platform Function App reaches the provisioner at this app's FQDN (set as ProvisionerUrl at G4).
    external_enabled = true
    target_port      = 8080
    transport        = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "provisioner"
      image  = "${azurerm_container_registry.provisioner.login_server}/${var.provisioner_image}:${var.provisioner_image_tag}"
      cpu    = 0.5
      memory = "1Gi"

      # DefaultAzureCredential selects the dedicated UAMI. All other settings — Hosting__*,
      # Provisioner__InboundKey, Platform__* — are set out-of-band from Key Vault (kv-aisdlc-81c0) at
      # deploy time (see the runbook), not committed here.
      env {
        name  = "AZURE_CLIENT_ID"
        value = azurerm_user_assigned_identity.provisioner.client_id
      }
    }
  }

  lifecycle {
    # The running image tag and the out-of-band env vars are managed by the deploy step / runbook, not
    # Terraform — don't let an apply revert them.
    ignore_changes = [
      template[0].container[0].image,
      template[0].container[0].env,
    ]
  }
}

output "provisioner_base_url" {
  description = "Provisioner Container App base URL — the platform sets this as ProvisionerUrl at G4 wiring."
  value       = "https://${azurerm_container_app.provisioner.ingress[0].fqdn}"
}
