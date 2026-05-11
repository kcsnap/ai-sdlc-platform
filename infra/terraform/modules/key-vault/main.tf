resource "azurerm_key_vault" "this" {
  name                       = var.name
  resource_group_name        = var.resource_group_name
  location                   = var.location
  tenant_id                  = var.tenant_id
  sku_name                   = "standard"
  soft_delete_retention_days = 7
  purge_protection_enabled   = false
  tags                       = var.tags
}

resource "azurerm_key_vault_access_policy" "readers" {
  for_each     = toset(var.secret_reader_principal_ids)
  key_vault_id = azurerm_key_vault.this.id
  tenant_id    = var.tenant_id
  object_id    = each.value

  secret_permissions = ["Get", "List"]
}

resource "azurerm_key_vault_access_policy" "officers" {
  for_each     = toset(var.secret_officer_principal_ids)
  key_vault_id = azurerm_key_vault.this.id
  tenant_id    = var.tenant_id
  object_id    = each.value

  secret_permissions = ["Get", "List", "Set", "Delete", "Purge", "Recover"]
}
