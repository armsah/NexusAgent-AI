resource "azurerm_container_app" "api" {
  count = var.deploy_api ? 1 : 0

  name                         = "ca-${local.name}-api"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.main.id]
  }

  registry {
    server   = azurerm_container_registry.main.login_server
    identity = azurerm_user_assigned_identity.main.id
  }

  ingress {
    external_enabled = true
    target_port      = 8080

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  template {
    min_replicas = 0
    max_replicas = 1

    container {
      name   = "api"
      image  = var.container_image
      cpu    = 0.5
      memory = "1Gi"
    }
  }

  depends_on = [
    azurerm_role_assignment.acr_pull
  ]
}

resource "azurerm_container_app" "worker" {
  count = var.deploy_api ? 1 : 0

  name                         = "ca-${local.name}-worker"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.main.id]
  }

  registry {
    server   = azurerm_container_registry.main.login_server
    identity = azurerm_user_assigned_identity.main.id
  }

  template {
    min_replicas = 0
    max_replicas = 1

    container {
      name   = "worker"
      image  = var.container_image
      cpu    = 0.5
      memory = "1Gi"
    }
  }

  depends_on = [
    azurerm_role_assignment.acr_pull
  ]
}