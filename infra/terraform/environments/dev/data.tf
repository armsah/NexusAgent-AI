resource "azurerm_postgresql_flexible_server" "main" {
  count = var.deploy_data_services ? 1 : 0

  name                   = "psql-${local.name}"
  resource_group_name    = azurerm_resource_group.main.name
  location               = azurerm_resource_group.main.location
  version                = "16"
  administrator_login    = var.postgres_admin_login
  administrator_password = var.postgres_admin_password

  sku_name   = "B_Standard_B1ms"
  storage_mb = 32768

  backup_retention_days = 7

  tags = local.tags

  lifecycle {
    precondition {
      condition     = var.postgres_admin_password != null && length(coalesce(var.postgres_admin_password, "")) >= 8
      error_message = "A PostgreSQL administrator password of at least 8 characters is required when deploying the database."
    }
  }
}

resource "azurerm_postgresql_flexible_server_database" "main" {
  count = var.deploy_data_services ? 1 : 0

  name      = "nexusagent"
  server_id = azurerm_postgresql_flexible_server.main[0].id

  charset   = "UTF8"
  collation = "en_US.utf8"
}

resource "azurerm_servicebus_namespace" "main" {
  name                = "sb-${local.name}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location

  sku                = "Standard"
  local_auth_enabled = false

  tags = local.tags
}

resource "azurerm_servicebus_queue" "workflows" {
  name         = "workflows"
  namespace_id = azurerm_servicebus_namespace.main.id

  max_delivery_count = 10
}

resource "azurerm_storage_account" "documents" {
  name                     = replace("st${local.name}", "-", "")
  resource_group_name      = azurerm_resource_group.main.name
  location                 = azurerm_resource_group.main.location
  account_tier             = "Standard"
  account_replication_type = "LRS"

  min_tls_version                 = "TLS1_2"
  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = false
  default_to_oauth_authentication = true

  tags = local.tags
}

resource "azurerm_storage_container" "documents" {
  name                  = "documents"
  storage_account_id    = azurerm_storage_account.documents.id
  container_access_type = "private"
}