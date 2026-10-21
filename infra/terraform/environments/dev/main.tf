locals {
  name = "${var.prefix}-${var.environment}"

  tags = merge(var.tags, {
    project     = "NexusAgent-AI"
    environment = var.environment
    managed_by  = "terraform"
  })
}

resource "azurerm_resource_group" "main" {
  name     = "rg-${local.name}"
  location = var.location

  tags = local.tags
}

resource "azurerm_log_analytics_workspace" "main" {
  name                = "log-${local.name}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name

  sku               = "PerGB2018"
  retention_in_days = 30
  daily_quota_gb    = 1

  tags = local.tags
}

resource "azurerm_application_insights" "main" {
  name                = "appi-${local.name}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name

  application_type     = "web"
  daily_data_cap_in_gb = 1

  workspace_id = azurerm_log_analytics_workspace.main.id

  tags = local.tags
}
