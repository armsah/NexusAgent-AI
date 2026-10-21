output "resource_group_name" {
  description = "Azure resource group name"
  value       = azurerm_resource_group.main.name
}

output "log_analytics_workspace_id" {
  description = "Log Analytics workspace resource ID"
  value       = azurerm_log_analytics_workspace.main.id
}

output "application_insights_name" {
  description = "Application Insights resource name"
  value       = azurerm_application_insights.main.name
}

output "application_insights_connection_string" {
  description = "Application Insights connection string"
  value       = azurerm_application_insights.main.connection_string
  sensitive   = true
}

output "container_registry_login_server" {
  description = "Azure Container Registry login server"
  value       = azurerm_container_registry.main.login_server
}

output "container_app_environment_id" {
  description = "Azure Container Apps environment ID"
  value       = azurerm_container_app_environment.main.id
}

output "managed_identity_client_id" {
  description = "User-assigned managed identity client ID"
  value       = azurerm_user_assigned_identity.main.client_id
}

output "key_vault_uri" {
  description = "Azure Key Vault URI"
  value       = azurerm_key_vault.main.vault_uri
}

output "servicebus_namespace_name" {
  description = "Azure Service Bus namespace name"
  value       = azurerm_servicebus_namespace.main.name
}

output "storage_account_name" {
  description = "Document storage account name"
  value       = azurerm_storage_account.documents.name
}

output "api_fqdn" {
  description = "API Container App hostname, when deployed"
  value       = try(azurerm_container_app.api[0].ingress[0].fqdn, null)
}

output "worker_container_app_id" {
  description = "Worker Container App ID, when deployed"
  value       = try(azurerm_container_app.worker[0].id, null)
}

output "postgres_server_fqdn" {
  description = "PostgreSQL server hostname, when deployed"
  value       = try(azurerm_postgresql_flexible_server.main[0].fqdn, null)
}

output "ai_search_endpoint" {
  description = "Azure AI Search endpoint, when deployed"
  value       = try("https://${azurerm_search_service.main[0].name}.search.windows.net", null)
}

output "azure_openai_endpoint" {
  description = "Azure OpenAI endpoint, when deployed"
  value       = try(azurerm_cognitive_account.openai[0].endpoint, null)
}