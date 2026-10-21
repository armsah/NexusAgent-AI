resource "azurerm_search_service" "main" {
  count = var.enable_ai_services ? 1 : 0

  name                = "srch-${local.name}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location

  sku             = "basic"
  replica_count   = 1
  partition_count = 1

  local_authentication_enabled = false

  tags = local.tags
}

resource "azurerm_cognitive_account" "openai" {
  count = var.enable_ai_services ? 1 : 0

  name                = "oai-${local.name}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location

  kind     = "OpenAI"
  sku_name = "S0"

  custom_subdomain_name = "oai-${local.name}"

  local_auth_enabled = false

  tags = local.tags
}