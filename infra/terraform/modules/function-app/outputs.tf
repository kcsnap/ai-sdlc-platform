output "id" {
  value = azapi_resource.function_app.id
}

output "name" {
  value = azapi_resource.function_app.name
}

output "default_hostname" {
  value = azapi_resource.function_app.output.properties.defaultHostName
}
