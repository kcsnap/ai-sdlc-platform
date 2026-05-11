output "id" {
  value = azurerm_linux_function_app.this.id
}

output "name" {
  value = azurerm_linux_function_app.this.name
}

output "default_hostname" {
  value = azurerm_linux_function_app.this.default_hostname
}

output "principal_id" {
  value = azurerm_linux_function_app.this.identity[0].principal_id
}
